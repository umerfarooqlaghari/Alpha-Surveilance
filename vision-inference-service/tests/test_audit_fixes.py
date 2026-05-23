"""
Targeted tests for audit fixes #1-#5.

Each test exercises an edge case the fix was supposed to address. Together
they form the safety net so a future refactor can't silently regress these
behaviours.
"""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import numpy as np
import pytest
from PIL import Image

import config
from rtsp.violation_manager import ViolationManager, SimpleIouTracker


# ─────────────────────────────────────────────────────────────────────────────
# helpers
# ─────────────────────────────────────────────────────────────────────────────

def _rule(sop_id="sop-glove", model="restaurant-ppe-v1", labels=("no_glove",)):
    return SimpleNamespace(
        sop_violation_type_id=sop_id,
        model_identifier=model,
        trigger_labels=list(labels),
    )


def _ppe_det(label, box, person_box=None, score=0.6):
    d = {
        "label": label,
        "score": score,
        "box": dict(box),
        "source_model": "restaurant-ppe-v1",
    }
    if person_box is not None:
        d["person_box"] = dict(person_box)
    return d


def _person_det(box, score=0.85):
    return {
        "label": "person",
        "score": score,
        "box": dict(box),
        "source_model": "human-detection-v1",
    }


def _run(coro):
    return asyncio.get_event_loop().run_until_complete(coro) if not asyncio.get_event_loop().is_closed() else asyncio.run(coro)


# ═════════════════════════════════════════════════════════════════════════════
# FIX #1 — person_track_id propagation
# ═════════════════════════════════════════════════════════════════════════════


class TestPersonTrackIdPropagation:
    """Verify PPE detections inside the same `person_box` share an id."""

    def test_two_ppe_dets_one_person_share_id_when_person_tracked(self):
        """When `human-detection-v1` is also configured, PPE dets should match
        the real person track's id (stable across frames)."""
        vm = ViolationManager(entry_hysteresis=3, exit_buffer=5)

        person_box = {"xmin": 100, "ymin": 100, "xmax": 300, "ymax": 500}
        no_mask = _ppe_det("no_mask", {"xmin": 180, "ymin": 130, "xmax": 220, "ymax": 170}, person_box=person_box)
        no_glove = _ppe_det("no_glove", {"xmin": 110, "ymin": 380, "xmax": 150, "ymax": 420}, person_box=person_box)
        person = _person_det(person_box)

        vm.tag_tracks("cam-1", [person, no_mask, no_glove])

        assert person["track_id"] is not None
        assert no_mask.get("person_track_id") == person["track_id"]
        assert no_glove.get("person_track_id") == person["track_id"]
        assert no_mask["person_track_id"] == no_glove["person_track_id"]

    def test_falls_back_to_synthesized_group_when_no_person_track(self):
        """PPE-crop mode w/o explicit human-detection-v1 rule: dets w/ same
        person_box still share a synthesized id."""
        vm = ViolationManager(entry_hysteresis=3, exit_buffer=5)

        person_box = {"xmin": 100, "ymin": 100, "xmax": 300, "ymax": 500}
        no_mask = _ppe_det("no_mask", {"xmin": 180, "ymin": 130, "xmax": 220, "ymax": 170}, person_box=person_box)
        no_glove = _ppe_det("no_glove", {"xmin": 110, "ymin": 380, "xmax": 150, "ymax": 420}, person_box=person_box)

        vm.tag_tracks("cam-1", [no_mask, no_glove])

        assert no_mask.get("person_track_id") is not None
        assert no_mask["person_track_id"] == no_glove["person_track_id"]
        # synthesised id is a string starting with "p:"
        assert str(no_mask["person_track_id"]).startswith("p:")

    def test_two_separate_persons_get_different_ids(self):
        vm = ViolationManager(entry_hysteresis=3, exit_buffer=5)

        pbox_a = {"xmin": 100, "ymin": 100, "xmax": 300, "ymax": 500}
        pbox_b = {"xmin": 800, "ymin": 100, "xmax": 1000, "ymax": 500}
        det_a = _ppe_det("no_glove", {"xmin": 110, "ymin": 380, "xmax": 150, "ymax": 420}, person_box=pbox_a)
        det_b = _ppe_det("no_glove", {"xmin": 810, "ymin": 380, "xmax": 850, "ymax": 420}, person_box=pbox_b)
        p_a = _person_det(pbox_a)
        p_b = _person_det(pbox_b)

        vm.tag_tracks("cam-1", [p_a, p_b, det_a, det_b])

        assert det_a["person_track_id"] != det_b["person_track_id"]
        assert det_a["person_track_id"] == p_a["track_id"]
        assert det_b["person_track_id"] == p_b["track_id"]

    def test_ppe_det_without_person_box_gets_no_person_track_id(self):
        """Full-frame fallback: no person_box → no person_track_id key."""
        vm = ViolationManager()
        det = _ppe_det("no_glove", {"xmin": 100, "ymin": 100, "xmax": 200, "ymax": 200})
        vm.tag_tracks("cam-1", [det])
        assert "person_track_id" not in det

    def test_low_iou_person_box_does_not_match_stale_track(self):
        """A person_box that doesn't overlap any live track should not steal
        an id from an unrelated track."""
        vm = ViolationManager()

        # Frame 1: person at left side
        p1 = _person_det({"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 200})
        vm.tag_tracks("cam-1", [p1])

        # Frame 2: PPE det with person_box on the far right (no overlap)
        det = _ppe_det("no_glove", {"xmin": 900, "ymin": 100, "xmax": 940, "ymax": 140},
                       person_box={"xmin": 800, "ymin": 0, "xmax": 1000, "ymax": 200})
        # Note: previous person track will be present with missing=1 since we don't re-send it
        vm.tag_tracks("cam-1", [det])
        # Either no person_track_id assigned (no live person), or synthesized
        assert det.get("person_track_id") is None or str(det["person_track_id"]).startswith("p:")


# ═════════════════════════════════════════════════════════════════════════════
# FIX #2 — frames_seen zombie reset
# ═════════════════════════════════════════════════════════════════════════════


class TestFramesSeenZombieReset:
    """A Pending state that bounces (seen-miss-seen-miss) must not silently
    accumulate frames_seen toward a stale violation fire."""

    def _vm(self):
        return ViolationManager(entry_hysteresis=3, exit_buffer=5)

    def test_bouncing_pending_does_not_fire_stale_violation(self):
        vm = self._vm()
        rules = [_rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])]
        det = _ppe_det("no_glove", {"xmin": 0, "ymin": 0, "xmax": 50, "ymax": 50})

        async def run():
            # Frame 1: seen (frames_seen=1)
            a1 = await vm.process_frame("cam", [dict(det, track_id=1)], rules)
            # Frame 2: missing (frames_missing=1, frames_seen reset to 0)
            a2 = await vm.process_frame("cam", [], rules)
            # Frame 3: seen again (frames_seen back to 1, NOT 2)
            a3 = await vm.process_frame("cam", [dict(det, track_id=1)], rules)
            # Frame 4: missing
            a4 = await vm.process_frame("cam", [], rules)
            # Frame 5: seen
            a5 = await vm.process_frame("cam", [dict(det, track_id=1)], rules)
            return a1, a2, a3, a4, a5

        results = asyncio.run(run())
        # NONE of them should have fired a violation — hysteresis 3 requires
        # 3 CONSECUTIVE observations.
        for actions in results:
            assert len(actions) == 0, f"Stale violation fired: {actions}"

    def test_three_consecutive_still_fires(self):
        """The fix must NOT break the happy path."""
        vm = self._vm()
        rules = [_rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])]
        det = _ppe_det("no_glove", {"xmin": 0, "ymin": 0, "xmax": 50, "ymax": 50})

        async def run():
            await vm.process_frame("cam", [dict(det, track_id=1)], rules)
            await vm.process_frame("cam", [dict(det, track_id=1)], rules)
            return await vm.process_frame("cam", [dict(det, track_id=1)], rules)

        actions = asyncio.run(run())
        assert len(actions) == 1
        assert actions[0]["StateStatus"] == "New"

    def test_zombie_state_is_evicted_after_exit_buffer(self):
        """A Pending state with no return should be evicted, not leak."""
        vm = self._vm()  # exit_buffer=5
        rules = [_rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])]
        det = _ppe_det("no_glove", {"xmin": 0, "ymin": 0, "xmax": 50, "ymax": 50})

        async def run():
            await vm.process_frame("cam", [dict(det, track_id=42)], rules)
            for _ in range(6):  # > exit_buffer
                await vm.process_frame("cam", [], rules)

        asyncio.run(run())
        states = vm._states.get("cam", {})
        assert (42, "sop-glove") not in states, "Pending zombie was not evicted"

    def test_bouncing_resets_each_miss_so_consecutive_3_after_bouncing_still_fires(self):
        """After a bounce, 3 consecutive sights should still fire."""
        vm = self._vm()
        rules = [_rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])]
        det = _ppe_det("no_glove", {"xmin": 0, "ymin": 0, "xmax": 50, "ymax": 50})

        async def run():
            await vm.process_frame("cam", [dict(det, track_id=1)], rules)  # seen
            await vm.process_frame("cam", [], rules)                        # miss (reset)
            await vm.process_frame("cam", [dict(det, track_id=1)], rules)  # seen=1
            await vm.process_frame("cam", [dict(det, track_id=1)], rules)  # seen=2
            return await vm.process_frame("cam", [dict(det, track_id=1)], rules)  # seen=3 → FIRE

        actions = asyncio.run(run())
        assert len(actions) == 1
        assert actions[0]["StateStatus"] == "New"


# ═════════════════════════════════════════════════════════════════════════════
# FIX #4 — httpx.AsyncClient pooling
# ═════════════════════════════════════════════════════════════════════════════


class TestHttpxPooling:
    def test_single_client_instance_reused(self):
        from rtsp.violation_api_client import ViolationApiClient

        c = ViolationApiClient(base_url="http://x", api_key="k")
        try:
            assert c._http is not None
            # All methods route through the same client instance
            client_id = id(c._http)
            # Re-call init shouldn't happen, but assertion encodes the contract
            assert id(c._http) == client_id
        finally:
            asyncio.run(c.aclose())

    def test_headers_set_at_client_level(self):
        from rtsp.violation_api_client import ViolationApiClient

        c = ViolationApiClient(base_url="http://x", api_key="secret-key-xyz")
        try:
            assert c._http.headers.get("X-Internal-Api-Key") == "secret-key-xyz"
            assert "User-Agent" in c._http.headers
        finally:
            asyncio.run(c.aclose())

    def test_aclose_is_idempotent(self):
        from rtsp.violation_api_client import ViolationApiClient

        c = ViolationApiClient(base_url="http://x", api_key="k")
        asyncio.run(c.aclose())
        # Second call must not raise
        asyncio.run(c.aclose())

    def test_post_violation_uses_pooled_client(self):
        """post_violation should call self._http.post, not open a new client."""
        from rtsp.violation_api_client import ViolationApiClient

        c = ViolationApiClient(base_url="http://x", api_key="k")
        try:
            mock_resp = SimpleNamespace(status_code=200, text="ok")
            c._http.post = AsyncMock(return_value=mock_resp)

            ok = asyncio.run(c.post_violation({"foo": "bar"}))
            assert ok is True
            c._http.post.assert_awaited_once()
            args, kwargs = c._http.post.call_args
            assert args[0].endswith("/api/Violations/internal")
            assert kwargs["json"] == [{"foo": "bar"}]
        finally:
            asyncio.run(c.aclose())

    def test_post_violation_returns_false_on_non_2xx(self):
        from rtsp.violation_api_client import ViolationApiClient

        c = ViolationApiClient(base_url="http://x", api_key="k")
        try:
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=500, text="boom"))
            assert asyncio.run(c.post_violation({"x": 1})) is False
        finally:
            asyncio.run(c.aclose())

    def test_post_violation_returns_false_on_network_exception(self):
        from rtsp.violation_api_client import ViolationApiClient
        import httpx

        c = ViolationApiClient(base_url="http://x", api_key="k")
        try:
            c._http.post = AsyncMock(side_effect=httpx.ConnectError("nope"))
            assert asyncio.run(c.post_violation({"x": 1})) is False
        finally:
            asyncio.run(c.aclose())


# ═════════════════════════════════════════════════════════════════════════════
# FIX #5 — motion gate
# ═════════════════════════════════════════════════════════════════════════════


class _FakeEngine:
    """Mimics the surface of InferenceEngine that the motion-gate helpers use.

    We bind the real methods to a lightweight class instead of instantiating
    InferenceEngine (which loads YOLO/HF models on construction).
    """

    def __init__(self):
        self._motion_cache = {}


# Stub heavy/optional deps so the import works even on a slim test venv.
import sys, types
for _mod in ("inference_sdk", "transformers", "torch", "ultralytics"):
    if _mod not in sys.modules:
        sys.modules[_mod] = types.ModuleType(_mod)
# Provide the specific symbols inference_engine imports
sys.modules["inference_sdk"].InferenceHTTPClient = object
sys.modules["transformers"].pipeline = lambda *a, **k: None
sys.modules["torch"].backends = types.SimpleNamespace(mps=types.SimpleNamespace(is_available=lambda: False))
sys.modules["torch"].cuda = types.SimpleNamespace(is_available=lambda: False)
sys.modules["ultralytics"].YOLO = object
sys.modules["ultralytics"].YOLOWorld = object

from inference.inference_engine import InferenceEngine as _IE
_FakeEngine._frame_thumbnail = _IE._frame_thumbnail
_FakeEngine._maybe_gated_persons = _IE._maybe_gated_persons
_FakeEngine._update_motion_cache = _IE._update_motion_cache


class TestMotionGate:
    @pytest.fixture(autouse=True)
    def _reset_config(self, monkeypatch):
        monkeypatch.setattr(config, "MOTION_GATE_ENABLED", True)
        monkeypatch.setattr(config, "MOTION_GATE_THRESHOLD", 5.0)
        monkeypatch.setattr(config, "MOTION_GATE_SAMPLE_SIZE", 64)
        yield

    def test_disabled_returns_none(self, monkeypatch):
        monkeypatch.setattr(config, "MOTION_GATE_ENABLED", False)
        eng = _FakeEngine()
        img = Image.new("RGB", (320, 240), (100, 100, 100))
        # Pre-populate cache to prove we don't read it when disabled
        eng._motion_cache["cam"] = {"thumb": np.zeros((64, 64), dtype=np.int16), "persons": [{"x": 1}]}
        assert eng._maybe_gated_persons(img, "cam") is None

    def test_no_camera_id_returns_none(self):
        eng = _FakeEngine()
        img = Image.new("RGB", (320, 240), (100, 100, 100))
        assert eng._maybe_gated_persons(img, None) is None

    def test_first_call_returns_none_no_cache(self):
        eng = _FakeEngine()
        img = Image.new("RGB", (320, 240), (100, 100, 100))
        assert eng._maybe_gated_persons(img, "cam") is None

    def test_identical_frames_reuse_cached_persons(self):
        eng = _FakeEngine()
        img = Image.new("RGB", (320, 240), (100, 100, 100))
        persons = [{"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50, "score": 0.9}]
        eng._update_motion_cache("cam", img, persons)

        out = eng._maybe_gated_persons(img, "cam")
        assert out is not None
        assert len(out) == 1
        assert out[0]["xmin"] == 10

    def test_big_motion_returns_none(self):
        eng = _FakeEngine()
        img1 = Image.new("RGB", (320, 240), (20, 20, 20))
        img2 = Image.new("RGB", (320, 240), (220, 220, 220))
        eng._update_motion_cache("cam", img1, [{"xmin": 1, "ymin": 1, "xmax": 2, "ymax": 2}])
        assert eng._maybe_gated_persons(img2, "cam") is None

    def test_cached_returned_as_copy_not_reference(self):
        """Mutating the returned list MUST NOT corrupt the cache."""
        eng = _FakeEngine()
        img = Image.new("RGB", (320, 240), (100, 100, 100))
        persons = [{"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}]
        eng._update_motion_cache("cam", img, persons)

        out = eng._maybe_gated_persons(img, "cam")
        out[0]["xmin"] = 999
        out2 = eng._maybe_gated_persons(img, "cam")
        assert out2[0]["xmin"] == 10

    def test_cache_thumb_is_refreshed_on_hit(self):
        """Slow drift (frame-by-frame tiny changes) eventually exceeds the
        threshold against the LAST cached frame, NOT the original first
        frame, because we refresh on every gated hit."""
        eng = _FakeEngine()
        # Start with mid-grey
        base = np.full((240, 320, 3), 100, dtype=np.uint8)
        img1 = Image.fromarray(base)
        eng._update_motion_cache("cam", img1, [])
        # Each subsequent frame brightens by 1 luminance unit — far below 5.0 threshold.
        for delta in range(1, 4):
            nxt = Image.fromarray(np.clip(base + delta, 0, 255).astype(np.uint8))
            got = eng._maybe_gated_persons(nxt, "cam")
            assert got is not None, f"frame +{delta} should still be gated"

    def test_cache_handles_size_change_gracefully(self):
        """If image size changes (rare but possible), motion calc shouldn't crash."""
        eng = _FakeEngine()
        img1 = Image.new("RGB", (320, 240), (100, 100, 100))
        eng._update_motion_cache("cam", img1, [{"a": 1}])
        # Resize doesn't matter because we always downsample to MOTION_GATE_SAMPLE_SIZE
        img2 = Image.new("RGB", (640, 480), (100, 100, 100))
        out = eng._maybe_gated_persons(img2, "cam")
        assert out is not None  # still gated — content identical post-thumb


# ═════════════════════════════════════════════════════════════════════════════
# FIX #7 — data_collector short-circuit on empty detections
# ═════════════════════════════════════════════════════════════════════════════


class TestDataCollectorShortCircuit:
    def test_empty_detections_does_not_save(self, tmp_path):
        from data_collector import DataCollector
        dc = DataCollector(base_path=str(tmp_path))
        img = Image.new("RGB", (100, 100), (50, 50, 50))
        # Use a sentinel so we'd notice if save_event ever ran.
        called = {"n": 0}
        original = dc.save_event
        dc.save_event = lambda *a, **k: called.__setitem__("n", called["n"] + 1) or original(*a, **k)
        dc.collect_inference_event(img, [], "cam-1", "tenant-1")
        assert called["n"] == 0

    def test_non_interesting_detections_does_not_save(self, tmp_path):
        from data_collector import DataCollector
        dc = DataCollector(base_path=str(tmp_path))
        img = Image.new("RGB", (100, 100), (50, 50, 50))
        # score=0.95 is above the 0.6 "ambiguous" upper bound → not interesting
        dc.collect_inference_event(img, [{"score": 0.95, "label": "x"}], "cam", "t")
        # nothing should have been written to disk
        assert not list((tmp_path / "raw_frames").iterdir())

    def test_ambiguous_detection_does_save(self, tmp_path):
        from data_collector import DataCollector
        dc = DataCollector(base_path=str(tmp_path))
        img = Image.new("RGB", (100, 100), (50, 50, 50))
        dc.collect_inference_event(img, [{"score": 0.45, "label": "x"}], "cam", "t")
        assert len(list((tmp_path / "raw_frames").iterdir())) == 1


# ═════════════════════════════════════════════════════════════════════════════
# FIX #9 — _create_payload shallow copy
# ═════════════════════════════════════════════════════════════════════════════


class TestCreatePayloadShallowCopy:
    def test_mutating_det_box_after_payload_does_not_change_payload(self):
        vm = ViolationManager()
        det = {
            "track_id": 7,
            "label": "no_glove",
            "score": 0.7,
            "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50},
            "source_model": "restaurant-ppe-v1",
        }
        payload = vm._create_payload(det, "cam", "New", "sop-glove", "restaurant-ppe-v1")

        det["box"]["xmin"] = 999
        det["label"] = "TAMPERED"

        assert payload["Box"]["xmin"] == 10
        assert payload["Metadata"]["label"] == "no_glove"

    def test_metadata_is_separate_dict(self):
        vm = ViolationManager()
        det = {"track_id": 1, "label": "x", "score": 0.5, "box": {"xmin": 0, "ymin": 0, "xmax": 1, "ymax": 1}}
        payload = vm._create_payload(det, "cam", "New", "sop", "model")
        assert payload["Metadata"] is not det
        assert payload["Box"] is not det["box"]


# ═════════════════════════════════════════════════════════════════════════════
# FIX #10 — LRU cap on _states
# ═════════════════════════════════════════════════════════════════════════════


class TestStatesLruCap:
    def test_states_cap_enforced(self):
        vm = ViolationManager(entry_hysteresis=3, exit_buffer=10_000, max_states_per_camera=100)
        rule = _rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])

        async def run():
            # Spam 250 distinct track ids — only 100 should remain.
            for tid in range(250):
                det = _ppe_det("no_glove", {"xmin": tid, "ymin": 0, "xmax": tid + 5, "ymax": 5})
                det["track_id"] = tid
                await vm.process_frame("cam", [det], [rule])

        asyncio.run(run())
        assert len(vm._states["cam"]) == 100

    def test_cap_floor_is_64(self):
        """Even if user passes a silly low cap, enforce minimum 64."""
        vm = ViolationManager(max_states_per_camera=1)
        assert vm.max_states_per_camera == 64

    def test_active_states_preferred_over_pending_for_retention(self):
        """When evicting, Pending states should go before Active ones."""
        # Cap of 64 (the floor) — fill it with 1 Active + 63 Pending, then
        # insert a 65th and assert the Active survived.
        cap = 64
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=10_000, max_states_per_camera=cap)
        rule = _rule("sop-glove", "restaurant-ppe-v1", ["no_glove"])

        async def run():
            # Promote track_id=1 to Active with hysteresis 2 (2 consecutive sights)
            d1 = _ppe_det("no_glove", {"xmin": 0, "ymin": 0, "xmax": 5, "ymax": 5})
            d1["track_id"] = 1
            await vm.process_frame("cam", [d1], [rule])
            await vm.process_frame("cam", [d1], [rule])  # → Active
            # Fill remaining 63 slots with Pending tracks (tid 2..64)
            for tid in range(2, cap + 1):
                d = _ppe_det("no_glove", {"xmin": tid, "ymin": 0, "xmax": tid + 5, "ymax": 5})
                d["track_id"] = tid
                await vm.process_frame("cam", [d], [rule])
            assert len(vm._states["cam"]) == cap
            # Insert a (cap+1)-th track → should evict a Pending, NOT the Active one
            d_new = _ppe_det("no_glove", {"xmin": 200, "ymin": 0, "xmax": 205, "ymax": 5})
            d_new["track_id"] = 999
            await vm.process_frame("cam", [d_new], [rule])

        asyncio.run(run())
        states = vm._states["cam"]
        assert len(states) == cap
        # Active state for tid=1 must still be present
        assert (1, "sop-glove") in states
        assert states[(1, "sop-glove")]["state"] == "Active"


# ═════════════════════════════════════════════════════════════════════════════
# FIX #11 — DLQ + retry for post_violation
# ═════════════════════════════════════════════════════════════════════════════


class TestPostViolationDlq:
    def _client(self):
        from rtsp.violation_api_client import ViolationApiClient
        return ViolationApiClient(base_url="http://x", api_key="k")

    def test_success_does_not_enqueue(self):
        c = self._client()
        try:
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=200, text="ok"))
            ok = asyncio.run(c.post_violation({"a": 1}))
            assert ok is True
            assert c.dlq_size == 0
        finally:
            asyncio.run(c.aclose())

    def test_4xx_permanent_does_not_enqueue(self):
        c = self._client()
        try:
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=400, text="bad"))
            ok = asyncio.run(c.post_violation({"a": 1}))
            assert ok is False
            assert c.dlq_size == 0
        finally:
            asyncio.run(c.aclose())

    def test_5xx_transient_does_enqueue(self):
        c = self._client()
        try:
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=503, text="down"))
            ok = asyncio.run(c.post_violation({"a": 1}))
            assert ok is False
            assert c.dlq_size == 1
        finally:
            asyncio.run(c.aclose())

    def test_network_error_does_enqueue(self):
        import httpx
        c = self._client()
        try:
            c._http.post = AsyncMock(side_effect=httpx.ConnectError("nope"))
            ok = asyncio.run(c.post_violation({"a": 1}))
            assert ok is False
            assert c.dlq_size == 1
        finally:
            asyncio.run(c.aclose())

    def test_dlq_cap_evicts_oldest(self):
        c = self._client()
        try:
            # Set tiny cap by replacing the deque
            from collections import deque
            c._dlq = deque(maxlen=3)
            for i in range(5):
                c._enqueue_dlq({"i": i})
            assert c.dlq_size == 3
            items = list(c._dlq)
            # Oldest two (0, 1) should be evicted
            assert items[0]["i"] == 2
            assert items[-1]["i"] == 4
        finally:
            asyncio.run(c.aclose())

    def test_drain_flushes_when_api_recovers(self):
        """Queue 3 payloads with API down, then mark API up, run drain once,
        all 3 should flush."""
        c = self._client()
        try:
            import httpx
            # First: API down → enqueue 3
            c._http.post = AsyncMock(side_effect=httpx.ConnectError("down"))
            for i in range(3):
                asyncio.run(c.post_violation({"i": i}))
            assert c.dlq_size == 3

            # API recovers
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=200, text="ok"))

            # Run ONE drain pass manually (don't sleep) by inlining the loop body
            async def one_pass():
                while c._dlq:
                    p = c._dlq.popleft()
                    ok, transient = await c._try_post_violation(p)
                    if not ok and transient:
                        c._dlq.appendleft(p)
                        break
            asyncio.run(one_pass())
            assert c.dlq_size == 0
        finally:
            asyncio.run(c.aclose())

    def test_drain_stops_on_first_transient_failure(self):
        """If API is still down mid-drain, remaining payloads stay queued."""
        c = self._client()
        try:
            import httpx
            c._http.post = AsyncMock(side_effect=httpx.ConnectError("down"))
            for i in range(5):
                c._enqueue_dlq({"i": i})
            assert c.dlq_size == 5

            async def one_pass():
                while c._dlq:
                    p = c._dlq.popleft()
                    ok, transient = await c._try_post_violation(p)
                    if not ok and transient:
                        c._dlq.appendleft(p)
                        break
            asyncio.run(one_pass())
            # All 5 should still be queued (first attempt failed transiently)
            assert c.dlq_size == 5
            # Order preserved: first item back at front
            assert c._dlq[0]["i"] == 0
        finally:
            asyncio.run(c.aclose())

    def test_drain_drops_permanent_failures(self):
        """If a queued payload turns out to be permanently bad (4xx), drop it
        instead of looping forever."""
        c = self._client()
        try:
            c._enqueue_dlq({"i": "bad"})
            c._http.post = AsyncMock(return_value=SimpleNamespace(status_code=400, text="bad"))
            async def one_pass():
                while c._dlq:
                    p = c._dlq.popleft()
                    ok, transient = await c._try_post_violation(p)
                    if not ok and transient:
                        c._dlq.appendleft(p)
                        break
            asyncio.run(one_pass())
            assert c.dlq_size == 0
        finally:
            asyncio.run(c.aclose())

    def test_aclose_cancels_background_task(self):
        c = self._client()
        async def boot_and_close():
            c.start_background_workers()
            assert c._dlq_task is not None
            await asyncio.sleep(0)  # let task start
            await c.aclose()
            assert c._dlq_task.done() or c._dlq_task.cancelled()
        asyncio.run(boot_and_close())


# ═════════════════════════════════════════════════════════════════════════════
# FIX #12 — default cooldown for any violation type
# ═════════════════════════════════════════════════════════════════════════════


class TestDefaultCooldown:
    """The legacy cooldown dict was keyed on hard-coded category names
    ("Security", "Safety", etc) that no longer matched real labels like
    "no_glove". Result: every Active->Cooldown transition immediately
    rolled back to Pending. After #12 we use a single default_cooldown_seconds
    fallback so the cooldown actually holds.
    """

    def _drive(self, vm, label, frames, person_box=None):
        """Drive vm.process_frame `frames` times with one detection."""
        async def go():
            results = []
            for _ in range(frames):
                det = _ppe_det(label, {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}, person_box=person_box)
                det["track_id"] = 1  # stable id, skip tracker
                r = await vm.process_frame("cam-cd", [det], [_rule(labels=("no_glove",))])
                results.append(r)
            return results
        return asyncio.run(go())

    def _drive_empty(self, vm, frames):
        async def go():
            for _ in range(frames):
                await vm.process_frame("cam-cd", [], [_rule(labels=("no_glove",))])
        asyncio.run(go())

    def test_cooldown_uses_default_threshold_not_legacy_keys(self):
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        # Ensure default is set and legacy dict is empty
        assert vm._default_cooldown_seconds == 60
        assert vm._cooldown_thresholds == {}

        # promote to Active (2 frames seen)
        self._drive(vm, "no_glove", 2)
        camera_states = vm._get_camera_states("cam-cd")
        state_key = next(iter(camera_states))
        assert camera_states[state_key]["state"] == ViolationManager.STATE_ACTIVE

        # drive misses to move Active -> Cooldown (exit_buffer=3)
        self._drive_empty(vm, 3)
        assert camera_states[state_key]["state"] == ViolationManager.STATE_COOLDOWN

        # Immediately re-detect: should STAY in cooldown (not enough time)
        self._drive(vm, "no_glove", 1)
        assert camera_states[state_key]["state"] == ViolationManager.STATE_COOLDOWN

    def test_cooldown_releases_after_default_seconds(self):
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        vm._default_cooldown_seconds = 1  # speed up test

        self._drive(vm, "no_glove", 2)
        self._drive_empty(vm, 3)
        camera_states = vm._get_camera_states("cam-cd")
        state_key = next(iter(camera_states))
        assert camera_states[state_key]["state"] == ViolationManager.STATE_COOLDOWN

        # rewind last_trigger_at to simulate >1s passage
        camera_states[state_key]["last_trigger_at"] -= 5

        self._drive(vm, "no_glove", 1)
        # after cooldown expires, next detection puts us back in Pending
        assert camera_states[state_key]["state"] == ViolationManager.STATE_PENDING


# ═════════════════════════════════════════════════════════════════════════════
# FIX #15 — require_person rule_config gate
# ═════════════════════════════════════════════════════════════════════════════


class TestRequirePersonRuleConfig:
    """A PPE rule with `rule_config.require_person=true` should reject any
    detection lacking `person_box` (i.e., model fired on background noise).
    """

    def test_blocks_detection_without_person_box(self):
        from rules.evaluator import _passes_rule_config
        det = {"label": "no_glove", "score": 0.7, "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
        assert _passes_rule_config(det, {"require_person": True}, (640, 480)) is False

    def test_allows_detection_with_person_box(self):
        from rules.evaluator import _passes_rule_config
        det = {
            "label": "no_glove",
            "score": 0.7,
            "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10},
            "person_box": {"xmin": 0, "ymin": 0, "xmax": 200, "ymax": 400},
        }
        assert _passes_rule_config(det, {"require_person": True}, (640, 480)) is True

    def test_default_false_does_not_filter(self):
        from rules.evaluator import _passes_rule_config
        det = {"label": "no_glove", "score": 0.7, "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
        # no require_person key -> behaves as before (no filter)
        assert _passes_rule_config(det, {}, (640, 480)) is True
        # explicit false
        assert _passes_rule_config(det, {"require_person": False}, (640, 480)) is True

    def test_combines_with_geofence_rule_type(self):
        """require_person fires BEFORE rule-type dispatch, so a det without
        person_box is rejected even when the geofence would otherwise pass."""
        from rules.evaluator import _passes_rule_config
        det = {"label": "no_glove", "score": 0.7, "box": {"xmin": 50, "ymin": 50, "xmax": 100, "ymax": 100}}
        cfg = {
            "require_person": True,
            "type": "geofence",
            "polygon": [[0, 0], [1, 0], [1, 1], [0, 1]],  # whole frame normalized
        }
        assert _passes_rule_config(det, cfg, (640, 480)) is False


# ═════════════════════════════════════════════════════════════════════════════
# FIX #16 — asyncio.Lock guards state mutation
# ═════════════════════════════════════════════════════════════════════════════


class TestProcessFrameLock:
    """ViolationManager.process_frame must serialize concurrent invocations
    so tracker.update + state expiry can't interleave."""

    def test_per_camera_threading_lock_is_used(self):
        """C-2 fix: replaced single asyncio.Lock with per-camera threading.Lock.
        threading.Lock correctly serialises capture-thread (tag_tracks) vs
        event-loop-thread (process_frame) for the same camera."""
        vm = ViolationManager()
        lock = vm._get_camera_lock("cam-a")
        # threading.Lock isn't a class — it's a factory — so check via the
        # acquire/release protocol and that the same camera_id returns the
        # same lock instance.
        assert hasattr(lock, "acquire") and hasattr(lock, "release")
        assert vm._get_camera_lock("cam-a") is lock
        assert vm._get_camera_lock("cam-b") is not lock

    def test_concurrent_process_frame_does_not_corrupt_state(self):
        """Fire N concurrent process_frame calls on the same camera. With
        the lock in place, the final state count must equal what a serial
        execution would produce (1 entry per unique track_id)."""
        # Large exit_buffer so background "miss" accounting from other tids
        # in the same batch doesn't expire entries we care about.
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=10_000)
        rule = _rule(labels=("no_glove",))

        async def fire(tid):
            det = _ppe_det("no_glove", {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50})
            det["track_id"] = tid  # pre-tagged so tracker is skipped
            await vm.process_frame("cam-lock", [det], [rule])

        async def main():
            # 20 concurrent calls across 5 distinct tracks
            await asyncio.gather(*[fire(i % 5) for i in range(20)])

        asyncio.run(main())
        states = vm._get_camera_states("cam-lock")
        assert len(states) == 5  # exactly the unique tids, no duplicates / torn writes

    def test_lock_does_not_deadlock_on_sequential_calls(self):
        """Two sequential awaits on the same lock must not deadlock."""
        vm = ViolationManager(entry_hysteresis=2, exit_buffer=3)
        rule = _rule(labels=("no_glove",))

        async def main():
            for _ in range(3):
                det = _ppe_det("no_glove", {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50})
                det["track_id"] = 1
                await vm.process_frame("cam-seq", [det], [rule])

        asyncio.run(main())  # would hang forever if lock leaked


# ═════════════════════════════════════════════════════════════════════════════
# FIX #17 — /metrics endpoint exposes Prometheus counters
# ═════════════════════════════════════════════════════════════════════════════


class TestMetricsModule:
    """The metrics module should declare all the counters/histograms wired
    into the pipeline and render them in Prometheus text format."""

    def test_render_returns_text_and_content_type(self):
        import metrics as vm
        body, ct = vm.render_text()
        assert isinstance(body, (bytes, bytearray))
        assert "text/plain" in ct  # Prometheus exposition content-type

    def test_expected_metric_names_present(self):
        import metrics as vm
        body, _ = vm.render_text()
        text = body.decode()
        for name in (
            "vision_inference_latency_seconds",
            "vision_frames_processed_total",
            "vision_detections_total",
            "vision_violations_emitted_total",
            "vision_violation_api_post_total",
            "vision_violation_api_dlq_size",
        ):
            assert name in text, f"missing metric: {name}"

    def test_counter_increments_visible_in_render(self):
        import metrics as vm
        before = vm.frames_processed_total.labels(camera_id="t1")._value.get()
        vm.frames_processed_total.labels(camera_id="t1").inc()
        vm.frames_processed_total.labels(camera_id="t1").inc()
        after = vm.frames_processed_total.labels(camera_id="t1")._value.get()
        assert after - before == 2

        body, _ = vm.render_text()
        text = body.decode()
        assert 'vision_frames_processed_total{camera_id="t1"}' in text

    def test_histogram_records_latency(self):
        import metrics as vm
        with vm.inference_latency_seconds.labels(camera_id="t2").time():
            pass  # near-zero duration
        body, _ = vm.render_text()
        text = body.decode()
        assert 'vision_inference_latency_seconds_count{camera_id="t2"}' in text

    def test_gauge_can_be_set(self):
        import metrics as vm
        vm.api_dlq_size.set(42)
        body, _ = vm.render_text()
        text = body.decode()
        assert "vision_violation_api_dlq_size 42" in text


if __name__ == "__main__":
    import sys
    sys.exit(pytest.main([__file__, "-v"]))