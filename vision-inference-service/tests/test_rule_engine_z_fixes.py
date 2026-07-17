"""
Acceptance tests for the three verified rule-engine bugs (Z1 / Z2 / Z3).

Z1 — Geofence/dwell bypass:
    ViolationManager.process_frame re-matched evaluator output against ALL
    camera rules by (model, label) only, ignoring the sop_violation_type_id
    the evaluator stamped. A person OUTSIDE a geofence zone (passing only a
    sibling whole-frame rule with the same model+label) still fired the
    geofence SOP. Also, evaluator._dedupe collapsed violations across
    different SOPs sharing a box+label.

Z2 — Cooldown measured from activation, not exit:
    last_trigger_at was set once at Pending->Active and never refreshed, so a
    violation Active for longer than the cooldown threshold entered Cooldown
    already "expired" (and past the 2x eviction horizon) — it was evicted on
    the next frame and re-fired as "New" after any brief occlusion.

Z3 — Strictest rule censors all:
    _valid_detections pre-filtered with max() of all matching rule
    thresholds, so one 0.90-threshold rule discarded detections a 0.40 rule
    was entitled to see. Per-rule thresholds are now enforced at claim time.

These tests intentionally avoid torch/ultralytics/cv2-heavy code paths: they
only import rules.evaluator and rtsp.violation_manager.
"""
import asyncio
from types import SimpleNamespace

import pytest

import rtsp.violation_manager as vm_module
from rtsp.violation_manager import ViolationManager
from rules.evaluator import evaluate_violations, _dedupe


MODEL = "human-detection-v1"

# Pixel-space square zone covering x,y in [0, 100].
ZONE = [[0, 0], [100, 0], [100, 100], [0, 100]]
FRAME_SIZE = (640, 480)


def make_rule(
    sop=None,
    model=MODEL,
    labels=("person",),
    rule_config=None,
    min_conf=None,
    name="rule",
):
    return SimpleNamespace(
        name=name,
        sop_violation_type_id=sop,
        model_identifier=model,
        trigger_labels=list(labels),
        rule_config=rule_config or {},
        model_min_confidence=min_conf,
        model_type="",
    )


def make_det(
    label="person",
    score=0.9,
    box=None,
    source_model=MODEL,
    track_id=None,
    **extra,
):
    det = {
        "label": label,
        "score": score,
        "box": box or {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 90},
        "source_model": source_model,
    }
    if track_id is not None:
        det["track_id"] = track_id
    det.update(extra)
    return det


# bottom_center anchor of this box is (30, 90) -> INSIDE the zone
BOX_INSIDE = {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 90}
# bottom_center anchor of this box is (320, 380) -> OUTSIDE the zone
BOX_OUTSIDE = {"xmin": 300, "ymin": 300, "xmax": 340, "ymax": 380}


def run(coro):
    return asyncio.run(coro)


async def drive(vm, camera_id, detections, rules, frames=1):
    """Feed the same detections for N frames; return list of per-frame actions."""
    out = []
    for _ in range(frames):
        out.append(await vm.process_frame(camera_id, detections, rules))
    return out


class FakeClock:
    def __init__(self, start=1_000_000.0):
        self.now = start

    def time(self):
        return self.now

    def advance(self, seconds):
        self.now += seconds


@pytest.fixture
def clock(monkeypatch):
    """Patch time.time as seen by rtsp.violation_manager only."""
    c = FakeClock()
    monkeypatch.setattr(vm_module, "time", SimpleNamespace(time=c.time))
    return c


# ═════════════════════════════════════════════════════════════════════════════
# Z1 — SOP-aware matching in ViolationManager + SOP-aware dedupe in evaluator
# ═════════════════════════════════════════════════════════════════════════════


class TestZ1GeofenceBypass:
    def _rules(self):
        geofence = make_rule(
            sop="sop-A-geofence",
            rule_config={"type": "geofence", "polygon": ZONE, "coordinate_space": "pixel"},
            name="geofence-rule",
        )
        whole_frame = make_rule(sop="sop-B-wholeframe", name="whole-frame-rule")
        return [geofence, whole_frame]

    def test_person_outside_zone_does_not_fire_geofence_sop_end_to_end(self):
        """NEGATIVE: subject outside the zone. The evaluator only passes the
        whole-frame rule; the manager must post ONLY sop-B, never sop-A."""
        rules = self._rules()
        det = make_det(box=BOX_OUTSIDE, track_id=7)

        violations = evaluate_violations([det], rules, frame_size=FRAME_SIZE, camera_id="cam-z1")
        assert len(violations) == 1
        assert violations[0]["sop_violation_type_id"] == "sop-B-wholeframe"

        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        frames = run(drive(vm, "cam-z1", violations, rules, frames=3))
        posted = [a for frame in frames for a in frame]
        assert posted, "whole-frame SOP should have fired"
        posted_sops = {a["SopViolationTypeId"] for a in posted}
        assert posted_sops == {"sop-B-wholeframe"}
        assert "sop-A-geofence" not in posted_sops
        # No state may even exist for the geofence SOP.
        states = vm._get_camera_states("cam-z1")
        assert all(key[1] != "sop-A-geofence" for key in states)
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert len(new_actions) == 1
        assert new_actions[0]["SopViolationTypeId"] == "sop-B-wholeframe"

    def test_person_inside_zone_fires_both_sops(self):
        """POSITIVE: inside the zone both rules pass. The evaluator must emit
        one violation PER SOP (dedupe no longer collapses across SOPs) and the
        manager must fire both SOP ids."""
        rules = self._rules()
        det = make_det(box=BOX_INSIDE, track_id=8)

        violations = evaluate_violations([det], rules, frame_size=FRAME_SIZE, camera_id="cam-z1b")
        sops = {v["sop_violation_type_id"] for v in violations}
        assert sops == {"sop-A-geofence", "sop-B-wholeframe"}
        assert len(violations) == 2

        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        frames = run(drive(vm, "cam-z1b", violations, rules, frames=2))
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert {a["SopViolationTypeId"] for a in new_actions} == {
            "sop-A-geofence",
            "sop-B-wholeframe",
        }

    def test_dedupe_still_collapses_duplicates_within_same_sop(self):
        v = {
            "box": BOX_INSIDE,
            "violation_type": "person",
            "sop_violation_type_id": "sop-X",
        }
        # Jittered copy of the same physical detection (within the 8px grid).
        v_jitter = dict(v, box={"xmin": 11, "ymin": 11, "xmax": 51, "ymax": 91})
        assert len(_dedupe([v, v_jitter])) == 1

    def test_dedupe_keeps_legacy_no_sop_behavior(self):
        v1 = {"box": BOX_INSIDE, "violation_type": "person"}
        v2 = {"box": dict(BOX_INSIDE), "violation_type": "person"}
        assert len(_dedupe([v1, v2])) == 1

    def test_manager_falls_back_to_label_matching_without_sop_id(self):
        """Backward compat: raw detections with no sop_violation_type_id still
        match camera rules by label (and empty trigger_labels = wildcard)."""
        rule = make_rule(sop="sop-legacy", labels=("person",))
        wildcard = make_rule(sop="sop-wild", labels=())
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        det = make_det(track_id=3)  # no sop_violation_type_id key

        frames = run(drive(vm, "cam-z1c", [det], [rule, wildcard], frames=2))
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert {a["SopViolationTypeId"] for a in new_actions} == {"sop-legacy", "sop-wild"}

    def test_sop_stamped_violation_never_matches_sibling_wildcard_rule(self):
        """A violation stamped with sop-A must not create state for a sibling
        wildcard rule with a different SOP, even though the label matches."""
        sibling = make_rule(sop="sop-other", labels=())
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        det = make_det(track_id=4, sop_violation_type_id="sop-A-geofence")

        frames = run(drive(vm, "cam-z1d", [det], [sibling], frames=3))
        assert all(frame == [] for frame in frames)
        assert vm._get_camera_states("cam-z1d") == {}

    def test_mixed_sop_and_legacy_rules_keep_full_hysteresis(self):
        """A camera mixing a SOP-stamped rule and a legacy no-SOP rule on the
        same model+label yields two evaluator entries per physical detection.
        The manager must still require the FULL entry hysteresis for each
        (track, SOP) state — the same state must not advance twice per frame."""
        sop_rule = make_rule(sop="sop-mixed", labels=("person",))
        legacy_rule = make_rule(sop=None, labels=("person",))
        rules = [sop_rule, legacy_rule]
        det = make_det(track_id=6)

        violations = evaluate_violations([det], rules, frame_size=FRAME_SIZE)
        assert len(violations) == 2  # one stamped, one legacy

        vm = ViolationManager(entry_hysteresis=3, exit_buffer=3)
        frames = run(drive(vm, "cam-z1f", violations, rules, frames=3))
        # Frames 1 and 2 must stay silent (hysteresis=3), frame 3 fires.
        assert frames[0] == []
        assert frames[1] == []
        new_actions = [a for a in frames[2] if a["StateStatus"] == "New"]
        assert {a["SopViolationTypeId"] for a in new_actions} == {"sop-mixed", None}
        # And once Active, exactly ONE Update per state per frame.
        frame4 = run(drive(vm, "cam-z1f", violations, rules, frames=1))[0]
        assert len(frame4) == 2
        assert all(a["StateStatus"] == "Update" for a in frame4)

    def test_sop_stamped_violation_matches_only_its_own_rule(self):
        rules = [
            make_rule(sop="sop-A", labels=("person",)),
            make_rule(sop="sop-B", labels=("person",)),
        ]
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        det = make_det(track_id=5, sop_violation_type_id="sop-B")

        frames = run(drive(vm, "cam-z1e", [det], rules, frames=2))
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert len(new_actions) == 1
        assert new_actions[0]["SopViolationTypeId"] == "sop-B"
        assert all(key[1] == "sop-B" for key in vm._get_camera_states("cam-z1e"))


# ═════════════════════════════════════════════════════════════════════════════
# Z2 — cooldown measured from Active->Cooldown exit, not activation
# ═════════════════════════════════════════════════════════════════════════════


class TestZ2CooldownFromExit:
    CAM = "cam-z2"

    def _vm(self):
        return ViolationManager(entry_hysteresis=3, exit_buffer=2)

    def _rule(self):
        return make_rule(sop="sop-cd", labels=("person",))

    def _activate(self, vm, clock, det, rule, dt_per_frame=1.0):
        """Drive frames until Active; returns the New actions."""
        new_actions = []

        async def go():
            for _ in range(3):  # entry_hysteresis=3
                actions = await vm.process_frame(self.CAM, [det], [rule])
                new_actions.extend(a for a in actions if a["StateStatus"] == "New")
                clock.advance(dt_per_frame)

        run(go())
        return new_actions

    def test_long_active_violation_still_serves_full_cooldown_after_exit(self, clock):
        """CORE Z2 NEGATIVE: a violation Active for 2x the cooldown window
        must NOT be resurrected/evicted immediately after exiting — it gets a
        FULL cooldown measured from the exit."""
        vm, rule = self._vm(), self._rule()
        det = make_det(track_id=1)
        assert len(self._activate(vm, clock, det, rule)) == 1

        # Stay Active for 120 s = 2x default cooldown (60 s).
        async def stay_active():
            for _ in range(12):
                actions = await vm.process_frame(self.CAM, [det], [rule])
                assert all(a["StateStatus"] == "Update" for a in actions)
                clock.advance(10.0)

        run(stay_active())

        # Subject leaves: exit_buffer=2 missing frames -> Cooldown.
        async def leave():
            for _ in range(2):
                await vm.process_frame(self.CAM, [], [rule])
                clock.advance(1.0)

        run(leave())
        states = vm._get_camera_states(self.CAM)
        key = next(iter(states))
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN

        # Brief occlusion ends 5 s later: with the old activation-based clock
        # the state was already past threshold*2 -> evicted -> re-fired "New".
        clock.advance(5.0)

        async def reappear():
            return await vm.process_frame(self.CAM, [det], [rule])

        actions = run(reappear())
        assert actions == [], "must NOT re-fire during cooldown"
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN
        assert key in vm._get_camera_states(self.CAM), "must NOT be evicted"

        # Even repeated sightings within the window stay silent.
        async def keep_seeing():
            out = []
            for _ in range(3):
                clock.advance(5.0)
                out.extend(await vm.process_frame(self.CAM, [det], [rule]))
            return out

        assert run(keep_seeing()) == []
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN

    def test_cooldown_resurrects_after_full_window_since_exit(self, clock):
        vm, rule = self._vm(), self._rule()
        det = make_det(track_id=2)
        self._activate(vm, clock, det, rule)

        async def leave():
            for _ in range(2):
                await vm.process_frame(self.CAM, [], [rule])
                clock.advance(1.0)

        run(leave())
        states = vm._get_camera_states(self.CAM)
        key = next(iter(states))
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN

        # Full cooldown elapses since EXIT.
        clock.advance(61.0)

        async def resurface():
            frames = []
            for _ in range(4):
                frames.append(await vm.process_frame(self.CAM, [det], [rule]))
                clock.advance(1.0)
            return frames

        frames = run(resurface())
        # Frame 1: Cooldown -> Pending (frames_seen=1). Hysteresis=3 means the
        # 3rd consecutive sighting re-fires as "New".
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert len(new_actions) == 1
        assert new_actions[0]["SopViolationTypeId"] == "sop-cd"

    def test_cooldown_eviction_measured_from_exit(self, clock):
        vm, rule = self._vm(), self._rule()
        det = make_det(track_id=3)
        self._activate(vm, clock, det, rule)

        # Stay active well past the cooldown window before leaving.
        async def stay():
            for _ in range(10):
                await vm.process_frame(self.CAM, [det], [rule])
                clock.advance(15.0)  # 150 s active

        run(stay())

        async def miss(n):
            for _ in range(n):
                await vm.process_frame(self.CAM, [], [rule])

        run(miss(2))  # -> Cooldown (exit stamped here)
        states = vm._get_camera_states(self.CAM)
        key = next(iter(states))
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN

        # 119 s after exit (< 2 x 60): still retained.
        clock.advance(119.0)
        run(miss(1))
        assert key in vm._get_camera_states(self.CAM)

        # Past 2x cooldown since exit: evicted.
        clock.advance(3.0)
        run(miss(1))
        assert key not in vm._get_camera_states(self.CAM)

    def test_short_active_violation_cooldown_regression(self, clock):
        """Regression: normal short-lived violations keep the same observable
        behaviour (immediate re-appearance during cooldown stays silent)."""
        vm, rule = self._vm(), self._rule()
        det = make_det(track_id=4)
        self._activate(vm, clock, det, rule)  # ~3 s active

        async def leave_and_return():
            for _ in range(2):
                await vm.process_frame(self.CAM, [], [rule])
                clock.advance(1.0)
            return await vm.process_frame(self.CAM, [det], [rule])

        actions = run(leave_and_return())
        assert actions == []
        states = vm._get_camera_states(self.CAM)
        key = next(iter(states))
        assert states[key]["state"] == ViolationManager.STATE_COOLDOWN

    def test_action_payload_contract_unchanged(self, clock):
        """main.py consumes these actions — key set and values must be stable."""
        vm, rule = self._vm(), self._rule()
        det = make_det(track_id=5)
        new_actions = self._activate(vm, clock, det, rule)
        assert len(new_actions) == 1
        payload = new_actions[0]
        assert set(payload.keys()) == {
            "TrackId", "Label", "Score", "Box", "StateStatus",
            "SopViolationTypeId", "ModelIdentifier", "Metadata",
        }
        assert payload["StateStatus"] == "New"
        assert payload["SopViolationTypeId"] == "sop-cd"
        assert payload["ModelIdentifier"] == MODEL
        assert payload["TrackId"] == 5


# ═════════════════════════════════════════════════════════════════════════════
# Z3 — per-rule confidence thresholds (strictest rule must not censor siblings)
# ═════════════════════════════════════════════════════════════════════════════


class TestZ3PerRuleConfidence:
    def _rules(self):
        strict = make_rule(sop="sop-strict", min_conf=0.90, name="strict")
        lenient = make_rule(sop="sop-lenient", min_conf=0.40, name="lenient")
        return [strict, lenient]

    def test_mid_score_passes_lenient_but_not_strict(self):
        """CORE Z3: score 0.50 must reach the 0.40 rule even though a sibling
        0.90 rule exists — and must NOT fire the 0.90 rule."""
        violations = evaluate_violations(
            [make_det(score=0.50)], self._rules(), frame_size=FRAME_SIZE
        )
        assert len(violations) == 1
        assert violations[0]["sop_violation_type_id"] == "sop-lenient"

    def test_high_score_passes_both_rules(self):
        violations = evaluate_violations(
            [make_det(score=0.95)], self._rules(), frame_size=FRAME_SIZE
        )
        assert {v["sop_violation_type_id"] for v in violations} == {
            "sop-strict",
            "sop-lenient",
        }

    def test_below_all_thresholds_suppressed(self):
        assert evaluate_violations(
            [make_det(score=0.30)], self._rules(), frame_size=FRAME_SIZE
        ) == []

    def test_exact_threshold_passes(self):
        violations = evaluate_violations(
            [make_det(score=0.40)], self._rules(), frame_size=FRAME_SIZE
        )
        assert [v["sop_violation_type_id"] for v in violations] == ["sop-lenient"]

    def test_rule_without_explicit_threshold_uses_model_default(self):
        """Fail-closed default preserved: human-detection-v1 falls back to
        MIN_CONFIDENCE_HUGGINGFACE (0.40 by default)."""
        import config

        rule = make_rule(sop="sop-default", min_conf=None)
        below = make_det(score=config.MIN_CONFIDENCE_HUGGINGFACE - 0.05)
        above = make_det(score=config.MIN_CONFIDENCE_HUGGINGFACE + 0.05)
        assert evaluate_violations([below], [rule], frame_size=FRAME_SIZE) == []
        got = evaluate_violations([above], [rule], frame_size=FRAME_SIZE)
        assert [v["sop_violation_type_id"] for v in got] == ["sop-default"]

    def test_geofence_rule_enforces_its_own_threshold(self):
        """The per-rule confidence gate applies on the geofence path too: a
        detection INSIDE the zone but below the geofence rule's threshold must
        not fire it, while the lenient whole-frame sibling still does."""
        strict_geofence = make_rule(
            sop="sop-geo-strict",
            min_conf=0.90,
            rule_config={"type": "geofence", "polygon": ZONE, "coordinate_space": "pixel"},
        )
        lenient_frame = make_rule(sop="sop-frame-lenient", min_conf=0.40)
        violations = evaluate_violations(
            [make_det(score=0.60, box=BOX_INSIDE)],
            [strict_geofence, lenient_frame],
            frame_size=FRAME_SIZE,
        )
        assert {v["sop_violation_type_id"] for v in violations} == {"sop-frame-lenient"}

    def test_restaurant_ppe_path_enforces_per_rule_threshold(self):
        strict = make_rule(
            sop="sop-ppe-strict",
            model="restaurant-ppe-v1",
            labels=("no-hairnet",),
            min_conf=0.90,
        )
        lenient = make_rule(
            sop="sop-ppe-lenient",
            model="restaurant-ppe-v1",
            labels=("no-hairnet",),
            min_conf=0.40,
        )
        det = make_det(
            label="no-hairnet",
            score=0.55,
            source_model="restaurant-ppe-v1",
            model_family="restaurant-ppe",
        )
        violations = evaluate_violations([det], [strict, lenient], frame_size=FRAME_SIZE)
        assert {v["sop_violation_type_id"] for v in violations} == {"sop-ppe-lenient"}

    def test_pest_path_enforces_per_rule_threshold(self):
        strict = make_rule(
            sop="sop-pest-strict",
            model="pest-detection-v1",
            labels=("cockroach",),
            min_conf=0.90,
        )
        lenient = make_rule(
            sop="sop-pest-lenient",
            model="pest-detection-v1",
            labels=("cockroach",),
            min_conf=0.30,
        )
        det = make_det(
            label="cockroach",
            score=0.45,
            source_model="pest-detection-v1",
            model_family="pest-detection",
        )
        violations = evaluate_violations([det], [strict, lenient], frame_size=FRAME_SIZE)
        assert {v["sop_violation_type_id"] for v in violations} == {"sop-pest-lenient"}

    def test_end_to_end_strict_sop_never_posted_for_mid_score(self):
        """Full pipeline: mid-confidence detection must post ONLY the lenient
        SOP id through the ViolationManager."""
        rules = self._rules()
        det = make_det(score=0.50, track_id=9)
        violations = evaluate_violations([det], rules, frame_size=FRAME_SIZE)

        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        frames = run(drive(vm, "cam-z3", violations, rules, frames=2))
        new_actions = [a for frame in frames for a in frame if a["StateStatus"] == "New"]
        assert len(new_actions) == 1
        assert new_actions[0]["SopViolationTypeId"] == "sop-lenient"
