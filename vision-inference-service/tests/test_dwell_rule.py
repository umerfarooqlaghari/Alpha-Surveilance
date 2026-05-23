"""
Tests for rules.dwell — the time-based stay-in-zone rule.
"""
import threading
import pytest
from rules.dwell import evaluate_dwell_rule, get_dwell_state, _get_dwell_store_entry, _DWELL_STORE, _DWELL_INIT_TIMES


@pytest.fixture(autouse=True)
def _isolate_dwell_store():
    """Clear the module-level dwell state store before and after each test so
    tests that use the same rule config (same content hash) don't bleed state
    into each other."""
    _DWELL_STORE.clear()
    _DWELL_INIT_TIMES.clear()
    yield
    _DWELL_STORE.clear()
    _DWELL_INIT_TIMES.clear()


SQUARE = [[100, 100], [200, 100], [200, 200], [100, 200]]

# CAM-002 style restricted zone: a tighter rectangle within the frame
# (e.g. the kitchen prep area on camera 002, coordinates in pixel space).
CAM002_ZONE = [[50, 60], [250, 60], [250, 180], [50, 180]]


def _det(xmin, ymin, xmax, ymax, score=0.9, track_id=None, label="person"):
    d = {"label": label, "score": score,
         "box": {"xmin": xmin, "ymin": ymin, "xmax": xmax, "ymax": ymax}}
    if track_id is not None:
        d["track_id"] = track_id
    return d


def _cfg(duration_s=5.0, mode="entry", anchor="centroid", polygon=None):
    return {"type": "dwell", "polygon": polygon or SQUARE,
            "duration_s": duration_s, "mode": mode, "anchor": anchor}


# Centroid (150, 150) is inside SQUARE (100..200, 100..200).
INSIDE_DET = _det(140, 140, 160, 160, track_id=1)
# Centroid (300, 300) is outside.
OUTSIDE_DET = _det(290, 290, 310, 310, track_id=2)


def test_dwell_requires_continuous_presence():
    cfg = _cfg(duration_s=5.0)

    # t=0: subject enters — arm timer, not yet violating.
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0) is False
    # t=4s: still not enough dwell.
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=4.0) is False
    # t=5s: dwell threshold reached.
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=5.0) is True
    # t=10s: still violating (no reset until subject leaves).
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=10.0) is True


def test_dwell_resets_when_subject_leaves():
    cfg = _cfg(duration_s=3.0)

    # t=0..2: accumulating.
    evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0)
    evaluate_dwell_rule(INSIDE_DET, cfg, now=2.0)
    # t=2.5: subject leaves the zone (same track_id, now outside).
    out = _det(290, 290, 310, 310, track_id=1)
    assert evaluate_dwell_rule(out, cfg, now=2.5) is False
    # t=2.5 onward: re-enter — timer must start fresh.
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=3.0) is False  # just re-armed
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=5.9) is False  # 2.9s < 3s
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=6.0) is True   # exactly 3s


def test_dwell_outside_zone_never_fires():
    cfg = _cfg(duration_s=1.0)
    for t in [0.0, 1.0, 2.0, 100.0]:
        assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=t) is False


def test_dwell_exit_mode_inverts_zone():
    # "Permitted" zone: violation only if OUTSIDE for duration_s.
    cfg = _cfg(duration_s=2.0, mode="exit")
    assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=0.0) is False  # just entered "violating" state
    assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=2.0) is True
    # Subject moves into the permitted zone — timer resets.
    inside = _det(140, 140, 160, 160, track_id=2)
    assert evaluate_dwell_rule(inside, cfg, now=2.1) is False


def test_dwell_exit_mode_startup_grace_suppresses_pre_existing_subjects():
    """I-3 regression: an exit-mode rule that starts streaming into a scene
    where a subject is ALREADY outside the zone must not fire ``duration_s``
    seconds later \u2014 the subject may have been outside for hours.
    During the grace window, suppress.  After the grace window, normal
    semantics resume so a subject that LEAVES the zone after init still
    triggers an alert ``duration_s`` seconds after they left."""
    cfg = _cfg(duration_s=2.0, mode="exit")

    # Subject is already outside when the rule arms (t=10.0).  init_at = 10.0,
    # entered_at = 10.0, both equal.  Grace suppresses for duration_s.
    assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=10.0) is False
    # During grace: even at the dwell threshold, suppress.
    assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=11.5) is False
    # Just past grace (init_at + duration_s = 12.0): the rule trusts its timer.
    # entered_at == init_at == 10.0, now=12.0 \u2192 2.0s elapsed \u2192 dwell met.
    assert evaluate_dwell_rule(OUTSIDE_DET, cfg, now=12.0) is True


def test_dwell_exit_mode_after_grace_subjects_leaving_zone_alert_normally():
    """I-3 sanity: a fresh subject that LEAVES the zone after the grace window
    should still trigger an alert ``duration_s`` seconds later \u2014 the grace
    only applies to subjects whose entered_at is at-or-before init_at."""
    cfg = _cfg(duration_s=2.0, mode="exit")

    # Initialize the rule with a subject inside (no exit-mode violation yet).
    inside = _det(140, 140, 160, 160, track_id=99)
    assert evaluate_dwell_rule(inside, cfg, now=0.0) is False
    # Wait past the grace window.
    assert evaluate_dwell_rule(inside, cfg, now=10.0) is False  # still inside

    # Subject steps outside at t=10.0 \u2192 entered_at=10.0 > init_at=0.0.
    new_subj = _det(0, 0, 5, 5, track_id=42)  # outside SQUARE
    assert evaluate_dwell_rule(new_subj, cfg, now=10.0) is False
    assert evaluate_dwell_rule(new_subj, cfg, now=12.0) is True


def test_dwell_per_track_independence():
    cfg = _cfg(duration_s=4.0)

    t1 = _det(140, 140, 160, 160, track_id=1)
    t2 = _det(141, 141, 161, 161, track_id=2)

    # Track 1 enters at t=0.
    evaluate_dwell_rule(t1, cfg, now=0.0)
    # Track 2 enters at t=2.
    evaluate_dwell_rule(t2, cfg, now=2.0)

    # t=4: track 1 reaches dwell, track 2 has only 2s.
    assert evaluate_dwell_rule(t1, cfg, now=4.0) is True
    assert evaluate_dwell_rule(t2, cfg, now=4.0) is False
    # t=6: both reach dwell.
    assert evaluate_dwell_rule(t1, cfg, now=6.0) is True
    assert evaluate_dwell_rule(t2, cfg, now=6.0) is True


def test_dwell_fallback_to_centroid_bucket_when_no_track_id():
    cfg = _cfg(duration_s=2.0)
    # No track_id: dwell should still accumulate using centroid bucket.
    det = _det(140, 140, 160, 160)  # no track_id
    assert "track_id" not in det
    assert evaluate_dwell_rule(det, cfg, now=0.0) is False
    assert evaluate_dwell_rule(det, cfg, now=2.0) is True


def test_dwell_bad_duration_fails_closed():
    # Negative duration — fail closed.
    cfg = {"type": "dwell", "polygon": SQUARE, "duration_s": -1.0}
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0) is False
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=1000.0) is False

    # Non-numeric — fail closed.
    cfg = {"type": "dwell", "polygon": SQUARE, "duration_s": "five seconds"}
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0) is False


def test_dwell_bad_polygon_fails_closed():
    cfg = {"type": "dwell", "polygon": [[1, 1], [2, 2]], "duration_s": 2.0}
    # <3 vertices — caught by shared spatial polygon builder, fail-closed.
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0) is False
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=100.0) is False


def test_dwell_state_stored_as_tuple_on_rule_config():
    """C-1 regression: state is now stored in the module-level _DWELL_STORE,
    keyed by (camera_id, rule_content_hash).  Values are still
    (entered_at, last_seen_at) tuples."""
    cfg = _cfg(duration_s=5.0)
    evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0)
    state = get_dwell_state("", cfg)
    assert state, "state dict must be non-empty after first inside-frame"
    entry = state.get("t:1")
    assert entry is not None, "track key 't:1' must be present in state"
    assert isinstance(entry, tuple), "state value must be a (entered_at, last_seen_at) tuple"
    entered_at, last_seen_at = entry
    assert entered_at == 0.0
    assert last_seen_at == 0.0


def test_dwell_last_seen_at_updated_every_frame():
    """Bug #1 regression: last_seen_at advances with each frame."""
    cfg = _cfg(duration_s=5.0)
    evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0)
    evaluate_dwell_rule(INSIDE_DET, cfg, now=3.0)
    state = get_dwell_state("", cfg)
    entered_at, last_seen_at = state["t:1"]
    assert entered_at == 0.0, "entered_at must not change while inside"
    assert last_seen_at == 3.0, "last_seen_at must reflect most recent frame"


def test_dwell_long_continuous_presence_never_resets():
    """
    Bug #1 regression: a subject that stays inside well beyond 2×duration_s
    must keep firing on every frame — the old GC evicted the track at
    entered_at + 2*duration_s, silently restarting the timer.

    Simulates a CAM-002 scenario: chef stands at prep bench for 60 s with
    a 5 s dwell threshold — every poll after t=5 must return True.
    """
    cfg = _cfg(duration_s=5.0)
    det = _det(140, 140, 160, 160, track_id=99)

    evaluate_dwell_rule(det, cfg, now=0.0)  # arm

    # Simulate 30 FPS for 120 s (3 600 frames).  Every frame after t=5 must
    # return True — no silent gap caused by premature GC eviction.
    violated_frames = 0
    for tick in range(1, 121):  # t = 1..120 seconds
        result = evaluate_dwell_rule(det, cfg, now=float(tick))
        if tick >= 5:
            assert result is True, (
                f"Dwell should still fire at t={tick}s but returned False. "
                "Likely the GC evicted the track using entered_at instead of last_seen_at."
            )
            violated_frames += 1

    assert violated_frames == 116  # t=5..120 inclusive


def test_dwell_gc_removes_truly_stale_tracks():
    """
    GC should evict tracks that have genuinely left and not been seen again
    for > duration_s seconds, keeping the state dict from growing unboundedly.
    CAM-002 style: two people enter, one leaves quickly, the other lingers.
    """
    cfg = _cfg(duration_s=5.0)

    lingerer = _det(140, 140, 160, 160, track_id=10)
    leaver   = _det(140, 140, 160, 160, track_id=20)

    # Both enter at t=0.
    evaluate_dwell_rule(lingerer, cfg, now=0.0)
    evaluate_dwell_rule(leaver, cfg, now=0.0)
    assert len(get_dwell_state("", cfg)) == 2

    # Leaver exits at t=2 — timer cleared immediately.
    leaver_outside = _det(300, 300, 320, 320, track_id=20)
    evaluate_dwell_rule(leaver_outside, cfg, now=2.0)
    assert "t:20" not in get_dwell_state("", cfg), "leaver's key should be popped on exit"

    # Lingerer keeps going, triggering GC calls.  At t=100, only lingerer's
    # key should remain (leaver was already popped; nothing else is stale).
    evaluate_dwell_rule(lingerer, cfg, now=100.0)
    state = get_dwell_state("", cfg)
    assert "t:10" in state
    assert len(state) == 1


def test_dwell_sub_second_duration():
    """Issue #6 regression: duration_s < 1 (e.g. 0.5 s) must be honoured."""
    cfg = _cfg(duration_s=0.5)
    det = _det(140, 140, 160, 160, track_id=5)

    assert evaluate_dwell_rule(det, cfg, now=0.0) is False    # armed
    assert evaluate_dwell_rule(det, cfg, now=0.4) is False    # 0.4 < 0.5
    assert evaluate_dwell_rule(det, cfg, now=0.5) is True     # exactly 0.5 s


def test_dwell_lock_stored_on_rule_config():
    """C-1 + Issue #7: after first call, a threading.Lock must be associated
    with this (camera_id, rule) pair in the module-level store."""
    cfg = _cfg(duration_s=5.0)
    evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0)
    _, lock = _get_dwell_store_entry("", cfg)
    assert isinstance(lock, type(threading.Lock()))


def test_dwell_thread_safe_concurrent_frames():
    """
    Issue #7: simulate concurrent per-frame calls on the same rule_config
    (as would happen during a brief hot-reload overlap) and verify that state
    remains consistent — no KeyError, no lost entries, no double-reset.
    """
    import concurrent.futures

    cfg = _cfg(duration_s=2.0)
    det = _det(140, 140, 160, 160, track_id=7)

    errors = []

    def process_frame(t):
        try:
            evaluate_dwell_rule(det, cfg, now=t)
        except Exception as exc:  # noqa: BLE001
            errors.append(exc)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(process_frame, float(i) * 0.1) for i in range(80)]
        concurrent.futures.wait(futures)

    assert not errors, f"Concurrent dwell calls raised: {errors}"
    # After all frames the subject should have been seen up to t=7.9 s.
    # At duration_s=2, the track must be firing (entered_at=0, last_seen≈7.9).
    assert evaluate_dwell_rule(det, cfg, now=8.0) is True


def test_dwell_cam002_prep_bench_scenario():
    """
    End-to-end CAM-002 scenario: chef enters the prep-bench zone and lingers.

    Zone: CAM002_ZONE (pixel space, 50..250 x, 60..180 y).
    Anchor: bottom_center (feet position).
    Duration: 30 s — alert if staff stands at the bench longer than 30 s
              without moving (e.g. blocking the area).

    Track centroid is (150, 120) → bottom_center = (150, 160) → inside zone.
    """
    cfg = {
        "type": "dwell",
        "polygon": CAM002_ZONE,
        "duration_s": 30.0,
        "mode": "entry",
        "anchor": "bottom_center",
    }
    # bottom_center of (140..160, 80..160): x=(140+160)/2=150, y=160 → inside
    chef = _det(140, 80, 160, 160, track_id=42, label="person")

    evaluate_dwell_rule(chef, cfg, now=0.0)     # arm

    assert evaluate_dwell_rule(chef, cfg, now=15.0) is False   # half-way
    assert evaluate_dwell_rule(chef, cfg, now=30.0) is True    # threshold reached
    assert evaluate_dwell_rule(chef, cfg, now=120.0) is True   # still firing after 2 min

    # Chef steps away — timer clears.
    away = _det(400, 400, 420, 420, track_id=42, label="person")
    assert evaluate_dwell_rule(away, cfg, now=121.0) is False

    # Returns — must re-arm from scratch.
    assert evaluate_dwell_rule(chef, cfg, now=122.0) is False  # re-armed
    assert evaluate_dwell_rule(chef, cfg, now=151.9) is False  # 29.9 s < 30 s
    assert evaluate_dwell_rule(chef, cfg, now=152.0) is True   # 30.0 s


def test_dwell_wrong_type_passes_through():
    # The dispatcher is responsible for routing; if a geofence config is
    # routed here by mistake, dwell.py returns True (let other filters decide).
    cfg = {"type": "geofence", "polygon": SQUARE}
    assert evaluate_dwell_rule(INSIDE_DET, cfg, now=0.0) is True
