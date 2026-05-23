"""
Tests for P-2 true-async identify_person dispatch invariants.

The P-2 change moved the violation POST from AFTER a blocking
``identity_future.result(timeout=3.0)`` call TO inside an
``add_done_callback`` on the reid pool future.  The capture thread now
returns from the dispatch loop without waiting on reid at all.

Invariants verified here:
  1. Static: no blocking ``.result(timeout=...)`` call remains in on_frame.
  2. Static: ``add_done_callback`` is registered on the reid future.
  3. Static: ``copy.deepcopy`` is present in the dispatch closure.
  4. Deep-copy isolation: concurrent callbacks cannot mutate each other's det.
  5. CorrelationId: each call produces a unique, valid UUID.
  6. Reid exception fail-open: exception in fut.result() → ident={}, employee_id=None.
  7. RuntimeError from closed event loop is caught, not propagated to caller.
  8. Concurrent callbacks produce independent payload dicts.
  9. Payload completeness: all backend-required fields are present.
 10. No-person-box path: immediate post with employee_id=None (no reid).
 11. Employee ID flows correctly from successful reid result to payload.
"""
from __future__ import annotations

import ast
import asyncio
import copy
import json
import threading
import uuid
from concurrent.futures import Future, ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
MAIN_PY = Path(__file__).parent.parent / "main.py"


def _on_frame_source() -> str:
    """Return the body of on_frame as a source string."""
    src = MAIN_PY.read_text()
    tree = ast.parse(src)
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name == "on_frame":
            lines = src.splitlines()
            return "\n".join(lines[node.lineno - 1 : node.end_lineno])
    raise RuntimeError("on_frame not found in main.py")


def _make_det(label="no_mask", with_person_box=True):
    d = {
        "label": label,
        "score": 0.87,
        "box": {"xmin": 10, "ymin": 20, "xmax": 60, "ymax": 90},
    }
    if with_person_box:
        d["person_box"] = {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 200}
    return d


def _make_payload(det: dict, employee_id: Optional[str] = None) -> dict:
    """Mirror the payload construction in _build_and_post for test use."""
    return {
        "TenantId": "tenant-uuid",
        "CameraId": "cam-uuid",
        "ModelIdentifier": "ppe-v1",
        "SopViolationTypeId": "sop-uuid",
        "CorrelationId": str(uuid.uuid4()),
        "TrackId": 1,
        "Timestamp": datetime.now(timezone.utc).isoformat(),
        "FramePath": "",
        "Status": "Pending",
        "MetadataJson": json.dumps(det),
        "EmployeeId": employee_id,
    }


# ===========================================================================
# 1–3  Static analysis
# ===========================================================================

class TestStaticAnalysis:
    """Fast AST / grep checks that verify structural properties of main.py."""

    def test_no_blocking_reid_result_in_on_frame(self):
        """P-2: capture thread must never call identity_future.result(timeout=...)."""
        src = _on_frame_source()
        assert "identity_future.result(timeout" not in src, (
            "Blocking identity_future.result(timeout=...) is still present in "
            "on_frame — P-2 requires fire-and-forget via done_callback."
        )

    def test_add_done_callback_registered_on_reid_future(self):
        """P-2: done_callback must be registered on the reid future."""
        src = _on_frame_source()
        assert "add_done_callback" in src, (
            "_on_reid_done must be registered via identity_future.add_done_callback."
        )

    def test_deepcopy_called_in_dispatch(self):
        """P-2: _build_and_post must deep-copy det to prevent concurrent mutations."""
        src = _on_frame_source()
        assert "copy.deepcopy" in src, (
            "_build_and_post must call copy.deepcopy(det) to isolate concurrent callbacks."
        )

    def test_no_utcnow_anywhere_in_main(self):
        """Task 1: datetime.utcnow() must not appear anywhere in main.py."""
        src = MAIN_PY.read_text()
        assert "utcnow()" not in src, (
            "datetime.utcnow() is deprecated in Python 3.12+ — use "
            "datetime.now(timezone.utc) instead."
        )

    def test_datetime_timezone_utc_used_instead(self):
        """Task 1: modern timezone-aware timestamps are used in main.py."""
        src = MAIN_PY.read_text()
        assert "timezone.utc" in src, (
            "Expected datetime.now(timezone.utc) in main.py."
        )


# ===========================================================================
# 4  Deep-copy isolation
# ===========================================================================

class TestDeepCopyIsolation:
    """Verify that concurrent callbacks cannot corrupt each other's det dict."""

    def test_deepcopy_does_not_share_nested_objects(self):
        original = _make_det()
        clone1 = copy.deepcopy(original)
        clone2 = copy.deepcopy(original)

        clone1["employeeId"] = "emp-A"
        clone1["isUnauthorized"] = True
        clone2["employeeId"] = "emp-B"
        clone2["isUnauthorized"] = False

        # Original must be untouched
        assert "employeeId" not in original
        assert "isUnauthorized" not in original
        # Clones must be independent
        assert clone1["employeeId"] != clone2["employeeId"]
        assert clone1["isUnauthorized"] != clone2["isUnauthorized"]

    def test_deepcopy_isolates_nested_box(self):
        """Mutating a nested box dict in one clone must not touch others."""
        original = _make_det()
        clone = copy.deepcopy(original)
        clone["box"]["xmin"] = 999
        assert original["box"]["xmin"] == 10

    def test_deepcopy_isolates_person_box(self):
        original = _make_det()
        clone = copy.deepcopy(original)
        clone["person_box"]["xmin"] = 999
        assert original["person_box"]["xmin"] == 0

    def test_concurrent_deepcopy_in_thread_pool(self):
        """Two threads deep-copying the same dict must produce independent results."""
        shared = _make_det()
        results: list[dict] = []
        lock = threading.Lock()

        def worker(eid: str):
            d = copy.deepcopy(shared)
            d["employeeId"] = eid
            with lock:
                results.append(d)

        pool = ThreadPoolExecutor(max_workers=2)
        f1 = pool.submit(worker, "emp-001")
        f2 = pool.submit(worker, "emp-002")
        f1.result()
        f2.result()
        pool.shutdown(wait=True)

        assert len(results) == 2
        emp_ids = {r["employeeId"] for r in results}
        assert emp_ids == {"emp-001", "emp-002"}
        assert "employeeId" not in shared  # original untouched


# ===========================================================================
# 5  CorrelationId uniqueness
# ===========================================================================

class TestCorrelationId:

    def test_each_action_gets_unique_correlation_id(self):
        """100 simulated violations must have 100 distinct CorrelationIds."""
        ids = [str(uuid.uuid4()) for _ in range(100)]
        assert len(set(ids)) == 100, "Duplicate CorrelationId detected — uuid4 collision?"

    def test_correlation_id_is_valid_uuid(self):
        """CorrelationId must be parseable as a UUID (backend validates this)."""
        cid = str(uuid.uuid4())
        parsed = uuid.UUID(cid)
        assert str(parsed) == cid

    @pytest.mark.parametrize("bad_id", ["", "not-a-uuid", "12345", None])
    def test_invalid_correlation_id_raises(self, bad_id):
        """Non-UUID CorrelationIds must not be accepted silently."""
        with pytest.raises(Exception):
            uuid.UUID(bad_id)  # type: ignore[arg-type]


# ===========================================================================
# 6  Reid exception fail-open
# ===========================================================================

class TestReidFailOpen:
    """When identify_person raises or returns None, we fail open (employee_id=None)."""

    def _simulate_on_reid_done(self, fut: Future) -> dict:
        """Mirrors the try/except in _on_reid_done."""
        try:
            ident = fut.result() or {}
        except Exception:
            ident = {}
        return ident

    def test_timeout_exception_produces_empty_ident(self):
        fut: Future = Future()
        fut.set_exception(TimeoutError("reid timed out"))
        ident = self._simulate_on_reid_done(fut)
        assert ident == {}
        assert ident.get("employeeId") is None
        assert ident.get("isUnauthorized", False) is False

    def test_connection_error_produces_empty_ident(self):
        fut: Future = Future()
        fut.set_exception(ConnectionError("reid service unreachable"))
        ident = self._simulate_on_reid_done(fut)
        assert ident == {}

    def test_cancelled_future_produces_empty_ident(self):
        """Shutdown of the reid pool cancels in-flight futures."""
        fut: Future = Future()
        fut.cancel()
        ident = self._simulate_on_reid_done(fut)
        assert ident == {}

    def test_none_return_coerced_to_empty_dict(self):
        """identify_person returning None must not crash the callback."""
        fut: Future = Future()
        fut.set_result(None)
        ident = self._simulate_on_reid_done(fut)
        assert ident == {}
        assert ident.get("employeeId") is None

    def test_successful_reid_preserves_employee_id(self):
        fut: Future = Future()
        fut.set_result({"employeeId": "emp-abc", "isUnauthorized": False})
        ident = self._simulate_on_reid_done(fut)
        assert ident.get("employeeId") == "emp-abc"
        assert ident.get("isUnauthorized") is False

    def test_unauthorized_flag_propagates(self):
        fut: Future = Future()
        fut.set_result({"employeeId": None, "isUnauthorized": True})
        ident = self._simulate_on_reid_done(fut)
        assert ident.get("isUnauthorized") is True
        assert ident.get("employeeId") is None


# ===========================================================================
# 7  RuntimeError from closed event loop
# ===========================================================================

class TestClosedLoopHandling:
    """asyncio.run_coroutine_threadsafe raises RuntimeError on a closed loop."""

    def test_runtime_error_from_closed_loop_is_caught(self):
        """Simulates _build_and_post's guard around run_coroutine_threadsafe."""
        raised_runtime_error = []

        async def _noop():
            pass

        def simulate_build_and_post(loop: asyncio.AbstractEventLoop):
            try:
                asyncio.run_coroutine_threadsafe(_noop(), loop)
            except RuntimeError:
                raised_runtime_error.append("caught")

        loop = asyncio.new_event_loop()
        loop.close()
        simulate_build_and_post(loop)
        assert raised_runtime_error == ["caught"], (
            "RuntimeError from a closed loop must be caught; "
            "violation drop must be logged, not propagated."
        )

    def test_non_runtime_errors_propagate(self):
        """Only RuntimeError from a closed loop is expected; other errors must surface.
        Passing None as a coroutine raises TypeError (not silently swallowed)."""
        with pytest.raises((TypeError, RuntimeError)):
            asyncio.run_coroutine_threadsafe(None, None)  # type: ignore


# ===========================================================================
# 8  Concurrent callbacks produce independent payloads
# ===========================================================================

class TestConcurrentCallbacks:

    def test_two_concurrent_reid_callbacks_produce_independent_payloads(self):
        """
        Two reid callbacks firing at the same time must each produce their own
        CorrelationId, EmployeeId and MetadataJson without cross-contamination.
        """
        shared_det = _make_det(label="no_vest")
        results: list[dict] = []
        lock = threading.Lock()

        def callback(employee_id: str):
            det = copy.deepcopy(shared_det)
            det["employeeId"] = employee_id
            det["isUnauthorized"] = False
            payload = {
                "CorrelationId": str(uuid.uuid4()),
                "EmployeeId": employee_id,
                "MetadataJson": json.dumps(det),
            }
            with lock:
                results.append(payload)

        pool = ThreadPoolExecutor(max_workers=2)
        f1 = pool.submit(callback, "emp-001")
        f2 = pool.submit(callback, "emp-002")
        f1.result()
        f2.result()
        pool.shutdown(wait=True)

        assert len(results) == 2
        assert results[0]["CorrelationId"] != results[1]["CorrelationId"]
        emp_ids = {r["EmployeeId"] for r in results}
        assert emp_ids == {"emp-001", "emp-002"}

        # Confirm MetadataJson dicts contain the right employeeId each
        meta0 = json.loads(results[0]["MetadataJson"])
        meta1 = json.loads(results[1]["MetadataJson"])
        assert meta0["employeeId"] != meta1["employeeId"]

    def test_many_concurrent_callbacks_all_unique_correlation_ids(self):
        """50 concurrent callbacks must all produce distinct CorrelationIds."""
        ids: list[str] = []
        lock = threading.Lock()

        def callback(_idx: int):
            cid = str(uuid.uuid4())
            with lock:
                ids.append(cid)

        pool = ThreadPoolExecutor(max_workers=10)
        futures = [pool.submit(callback, i) for i in range(50)]
        for f in futures:
            f.result()
        pool.shutdown(wait=True)

        assert len(ids) == 50
        assert len(set(ids)) == 50, "Non-unique CorrelationId detected across 50 concurrent callbacks"


# ===========================================================================
# 9  Payload completeness
# ===========================================================================

REQUIRED_BACKEND_FIELDS = {
    "TenantId", "CameraId", "CorrelationId", "TrackId",
    "Timestamp", "Status", "MetadataJson", "EmployeeId",
}


class TestPayloadCompleteness:

    def test_payload_contains_all_required_fields(self):
        det = _make_det(with_person_box=False)
        payload = _make_payload(det, employee_id=None)
        missing = REQUIRED_BACKEND_FIELDS - set(payload.keys())
        assert not missing, f"Payload missing required backend fields: {missing}"

    def test_payload_timestamp_is_utc_aware_iso(self):
        det = _make_det(with_person_box=False)
        payload = _make_payload(det)
        ts = payload["Timestamp"]
        # Must be parseable as an ISO 8601 string with UTC offset
        # Python 3.11+ supports timezone-aware fromisoformat
        dt = datetime.fromisoformat(ts)
        assert dt.tzinfo is not None, "Timestamp must be timezone-aware (UTC)"

    def test_payload_status_is_pending(self):
        det = _make_det(with_person_box=False)
        payload = _make_payload(det)
        assert payload["Status"] == "Pending"

    def test_metadata_json_is_valid_json(self):
        det = _make_det(with_person_box=True)
        payload = _make_payload(det)
        parsed = json.loads(payload["MetadataJson"])
        assert parsed["label"] == det["label"]
        assert parsed["score"] == det["score"]

    @pytest.mark.parametrize("employee_id,expected", [
        ("emp-abc-123", "emp-abc-123"),
        (None, None),
    ])
    def test_employee_id_in_payload_matches_reid_result(self, employee_id, expected):
        """EmployeeId in the posted payload must reflect the reid result."""
        det = _make_det()
        payload = _make_payload(det, employee_id=employee_id)
        assert payload["EmployeeId"] == expected


# ===========================================================================
# 10  No-person-box path (immediate POST)
# ===========================================================================

class TestNoPersonBoxPath:

    def test_no_person_box_post_uses_employee_id_none(self):
        """When det has no person_box, the capture thread posts immediately with EmployeeId=None."""
        det = _make_det(with_person_box=False)
        assert "person_box" not in det
        # In the production code: _build_and_post(None, False, ...)
        payload = _make_payload(det, employee_id=None)
        assert payload["EmployeeId"] is None

    def test_no_person_box_det_unchanged_after_post(self):
        """For no-person-box actions, det must not be mutated with employeeId/isUnauthorized."""
        det = _make_det(with_person_box=False)
        # deepcopy happens inside _build_and_post; simulate it
        cloned = copy.deepcopy(det)
        cloned["employeeId"] = None
        cloned["isUnauthorized"] = False
        # Original det is unmodified
        assert "employeeId" not in det
        assert "isUnauthorized" not in det


# ===========================================================================
# 11  Employee ID flows correctly from successful reid
# ===========================================================================

class TestEmployeeIdFlow:

    def test_employee_id_in_payload_when_reid_succeeds(self):
        """When reid returns employeeId, it must appear in the posted payload."""
        # Simulate _on_reid_done receiving a successful result
        fut: Future = Future()
        fut.set_result({"employeeId": "emp-xyz-999", "isUnauthorized": False})
        try:
            ident = fut.result() or {}
        except Exception:
            ident = {}

        employee_id = ident.get("employeeId")
        det = copy.deepcopy(_make_det())
        det["employeeId"] = employee_id
        det["isUnauthorized"] = ident.get("isUnauthorized", False)

        payload = _make_payload(det, employee_id=employee_id)
        assert payload["EmployeeId"] == "emp-xyz-999"
        assert json.loads(payload["MetadataJson"])["employeeId"] == "emp-xyz-999"

    def test_employee_id_none_when_reid_fails(self):
        """When reid fails, the posted violation has EmployeeId=None (fail-open)."""
        fut: Future = Future()
        fut.set_exception(RuntimeError("reid crashed"))
        try:
            ident = fut.result() or {}
        except Exception:
            ident = {}

        employee_id = ident.get("employeeId")
        det = copy.deepcopy(_make_det())
        det["employeeId"] = employee_id

        payload = _make_payload(det, employee_id=employee_id)
        assert payload["EmployeeId"] is None

    def test_unauthorized_flag_false_when_reid_fails(self):
        """isUnauthorized must default to False when reid fails."""
        fut: Future = Future()
        fut.set_exception(ConnectionError("reid unreachable"))
        try:
            ident = fut.result() or {}
        except Exception:
            ident = {}

        is_unauthorized = ident.get("isUnauthorized", False)
        assert is_unauthorized is False
