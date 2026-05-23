"""
tests/test_detection_schedules.py
Tests for the DetectionSchedule sleep-window logic.

Feature
-------
Cameras can have recurring UTC time windows during which AI inference is
suppressed ("detection sleep windows"). The Vision Service evaluates these
at frame-callback time via _is_in_sleep_window / _camera_is_asleep.

Design
------
DaysOfWeek bitmask (mirrors .NET DayOfWeek):
  Sunday=1, Monday=2, Tuesday=4, Wednesday=8, Thursday=16, Friday=32, Saturday=64
  0 or 127 → every day.

Overnight windows (StartTime > EndTime, e.g. 22:00 → 02:00) are supported.
The start boundary is inclusive; the end boundary is exclusive.

NOTE – day-of-week + overnight interaction
The day check uses the *current* day, not the day the overnight window started.
For a "Monday-only 22:00→02:00" schedule, Tuesday 01:00 will NOT be filtered
because the current day is Tuesday.  Use days=127 for overnight windows that
must cover the midnight boundary seamlessly across all days.
"""

from datetime import datetime, timezone

from rtsp.models import CameraConfig, DetectionScheduleItem
from rtsp.stream_client import _camera_is_asleep, _is_in_sleep_window

# ── Helpers ───────────────────────────────────────────────────────────────────


def _sched(
    start: str,
    end: str,
    days: int = 127,
    active: bool = True,
    label: str = "",
) -> DetectionScheduleItem:
    return DetectionScheduleItem(
        start_time=start,
        end_time=end,
        days_of_week=days,
        is_active=active,
        label=label,
    )


def _utc(year: int, month: int, day: int, hour: int, minute: int) -> datetime:
    return datetime(year, month, day, hour, minute, 0, tzinfo=timezone.utc)


# Reference week — 2026-05-18 is a Monday (verified):
#   Mon 2026-05-18, Tue 2026-05-19, Wed 2026-05-20
#   Thu 2026-05-21, Fri 2026-05-22, Sat 2026-05-23, Sun 2026-05-24
MON = (2026, 5, 18)
TUE = (2026, 5, 19)
WED = (2026, 5, 20)
THU = (2026, 5, 21)
FRI = (2026, 5, 22)
SAT = (2026, 5, 23)
SUN = (2026, 5, 24)


def _base_config(**overrides) -> CameraConfig:
    defaults = dict(
        camera_db_id="db-1",
        camera_id="CAM-01",
        tenant_id="t-1",
        tenant_name="Acme",
        rtsp_url="rtsp://192.168.1.10/stream",
    )
    defaults.update(overrides)
    return CameraConfig(**defaults)


# ── 1-6: Normal (intra-day) window ───────────────────────────────────────────


class TestNormalWindow:
    """08:00–18:00 same-day window (start <= end)."""

    def test_inside_window(self):
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 10, 0)) is True

    def test_before_start(self):
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 7, 0)) is False

    def test_after_end(self):
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 20, 0)) is False

    def test_at_start_inclusive(self):
        """Start boundary is inclusive — camera must be asleep exactly at start time."""
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 8, 0)) is True

    def test_at_end_exclusive(self):
        """End boundary is exclusive — camera must be awake at the exact end time."""
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 18, 0)) is False

    def test_one_minute_before_end(self):
        assert _is_in_sleep_window(_sched("08:00", "18:00"), _utc(*MON, 17, 59)) is True


# ── 7-15: Overnight window (the user's scenario: 10 PM → 2 AM) ───────────────


class TestOvernightWindow:
    """
    22:00 → 02:00 overnight window (start > end, crosses midnight).
    This is the canonical scenario: camera sleeps from 10 PM to 2 AM next day.
    """

    def test_night_side_23_00(self):
        """23:00 on the start night — must be inside the sleep window."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*MON, 23, 0)) is True

    def test_early_morning_01_00(self):
        """01:00 next morning — still inside the window (crosses midnight)."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*TUE, 1, 0)) is True

    def test_midday_outside(self):
        """12:00 (noon) — clearly outside the 22:00→02:00 window."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*MON, 12, 0)) is False

    def test_at_start_22_00_inclusive(self):
        """Exactly 22:00 — start is inclusive, camera must be asleep."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*MON, 22, 0)) is True

    def test_at_end_02_00_exclusive(self):
        """Exactly 02:00 — end is exclusive, camera must be awake."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*TUE, 2, 0)) is False

    def test_one_minute_before_start(self):
        """21:59 — one minute before window starts, camera awake."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*MON, 21, 59)) is False

    def test_one_minute_after_end(self):
        """02:01 — one minute after window ends, camera awake."""
        assert _is_in_sleep_window(_sched("22:00", "02:00"), _utc(*TUE, 2, 1)) is False

    def test_longer_overnight_22_to_06_midpoint(self):
        """22:00 → 06:00: 03:30 in the morning must still be asleep."""
        assert _is_in_sleep_window(_sched("22:00", "06:00"), _utc(*TUE, 3, 30)) is True

    def test_longer_overnight_22_to_06_after_end(self):
        """06:01 — just after 06:00 end, camera awake."""
        assert _is_in_sleep_window(_sched("22:00", "06:00"), _utc(*TUE, 6, 1)) is False


# ── 16-24: Day-of-week filtering ─────────────────────────────────────────────


class TestDayOfWeek:
    """
    Bitmask: Sun=1, Mon=2, Tue=4, Wed=8, Thu=16, Fri=32, Sat=64.
    """

    def test_correct_day_in_window(self):
        """Monday-only (bits=2), now is Monday inside window → asleep."""
        assert _is_in_sleep_window(_sched("08:00", "18:00", days=2), _utc(*MON, 10, 0)) is True

    def test_wrong_day_not_in_mask(self):
        """Monday-only (bits=2), now is Tuesday → awake regardless of time."""
        assert _is_in_sleep_window(_sched("08:00", "18:00", days=2), _utc(*TUE, 10, 0)) is False

    def test_days_zero_means_every_day(self):
        """DaysOfWeek=0 is treated as 'every day' (same as 127)."""
        assert _is_in_sleep_window(_sched("08:00", "18:00", days=0), _utc(*SAT, 10, 0)) is True

    def test_days_127_means_every_day(self):
        """DaysOfWeek=127 covers all seven days."""
        assert _is_in_sleep_window(_sched("08:00", "18:00", days=127), _utc(*SUN, 10, 0)) is True

    def test_weekdays_only_on_saturday(self):
        """Mon–Fri mask (2+4+8+16+32=62), now is Saturday → awake."""
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=62), _utc(*SAT, 23, 0)) is False

    def test_weekdays_only_on_friday_in_window(self):
        """Mon–Fri mask (62), now is Friday inside window → asleep."""
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=62), _utc(*FRI, 23, 0)) is True

    def test_weekends_only_on_monday(self):
        """Sat+Sun mask (64+1=65), now is Monday → awake."""
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=65), _utc(*MON, 23, 0)) is False

    def test_weekends_only_on_sunday_in_window(self):
        """Sat+Sun mask (65), now is Sunday inside window → asleep."""
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=65), _utc(*SUN, 23, 0)) is True

    def test_overnight_day_boundary_limitation(self):
        """
        Monday-only 22:00→02:00: Tuesday 01:00 does NOT match because the
        day check uses the current day (Tuesday), not the day the window started.
        This is documented expected behaviour — use days=127 for cross-midnight
        suppression that needs to work every day.
        """
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=2), _utc(*TUE, 1, 0)) is False

    def test_every_day_overnight_covers_tuesday_morning(self):
        """With days=127 the overnight window correctly suppresses Tuesday 01:00."""
        assert _is_in_sleep_window(_sched("22:00", "02:00", days=127), _utc(*TUE, 1, 0)) is True


# ── 25-33: _camera_is_asleep aggregation ─────────────────────────────────────


class TestCameraIsAsleep:
    """_camera_is_asleep: aggregates over all active schedules on a CameraConfig."""

    def test_no_schedules_camera_awake(self):
        cfg = _base_config()
        assert _camera_is_asleep(cfg, _utc(*MON, 10, 0)) is False

    def test_single_active_schedule_inside_window(self):
        cfg = _base_config(detection_schedules=[_sched("08:00", "18:00")])
        assert _camera_is_asleep(cfg, _utc(*MON, 10, 0)) is True

    def test_single_active_schedule_outside_window(self):
        cfg = _base_config(detection_schedules=[_sched("08:00", "18:00")])
        assert _camera_is_asleep(cfg, _utc(*MON, 20, 0)) is False

    def test_inactive_schedule_ignored(self):
        """An inactive schedule must never suppress detection — camera stays awake."""
        cfg = _base_config(detection_schedules=[_sched("08:00", "18:00", active=False)])
        assert _camera_is_asleep(cfg, _utc(*MON, 10, 0)) is False

    def test_all_inactive_schedules_camera_awake(self):
        cfg = _base_config(
            detection_schedules=[
                _sched("08:00", "18:00", active=False),
                _sched("20:00", "23:00", active=False),
            ]
        )
        assert _camera_is_asleep(cfg, _utc(*MON, 10, 0)) is False

    def test_multiple_schedules_one_matches(self):
        """Any single matching active schedule is sufficient to suppress inference."""
        cfg = _base_config(
            detection_schedules=[
                _sched("08:00", "12:00"),  # does not match 23:00
                _sched("22:00", "02:00"),  # matches 23:00
            ]
        )
        assert _camera_is_asleep(cfg, _utc(*MON, 23, 0)) is True

    def test_multiple_schedules_none_match(self):
        cfg = _base_config(
            detection_schedules=[
                _sched("08:00", "12:00"),
                _sched("14:00", "18:00"),
            ]
        )
        assert _camera_is_asleep(cfg, _utc(*MON, 13, 0)) is False

    def test_overnight_both_sides_canonical(self):
        """
        The canonical 10 PM → 2 AM scenario.
        Verify suppression at the night side (23:00), early morning (01:00),
        the exclusive end boundary (02:00), and just before the window (21:59).
        """
        cfg = _base_config(detection_schedules=[_sched("22:00", "02:00")])
        assert _camera_is_asleep(cfg, _utc(*MON, 23, 0)) is True, "23:00 must be asleep"
        assert _camera_is_asleep(cfg, _utc(*TUE, 1, 0)) is True, "01:00 next morning still asleep"
        assert _camera_is_asleep(cfg, _utc(*TUE, 2, 0)) is False, "02:00 (exclusive end) must be awake"
        assert _camera_is_asleep(cfg, _utc(*MON, 21, 59)) is False, "21:59 must be awake"

    def test_inactive_mixed_with_active_outside_window(self):
        """Inactive schedule that would match + active schedule that does not → awake."""
        cfg = _base_config(
            detection_schedules=[
                _sched("08:00", "18:00", active=False),  # inactive but would match
                _sched("22:00", "23:00"),  # active but 10:00 is outside
            ]
        )
        assert _camera_is_asleep(cfg, _utc(*MON, 10, 0)) is False


# ── 34-37: Edge cases ─────────────────────────────────────────────────────────


class TestEdgeCases:
    def test_malformed_start_time(self):
        """Malformed start time must not raise — returns False (safe default)."""
        s = _sched("not-a-time", "18:00")
        assert _is_in_sleep_window(s, _utc(*MON, 10, 0)) is False

    def test_malformed_end_time(self):
        """Malformed end time must not raise — returns False."""
        s = _sched("08:00", "bad")
        assert _is_in_sleep_window(s, _utc(*MON, 10, 0)) is False

    def test_start_equals_end_is_never_asleep(self):
        """
        00:00→00:00 has start == end so start <= end is True.
        The condition (current >= 00:00 AND current < 00:00) can never be
        satisfied → camera is always awake (effectively a no-op window).
        """
        s = _sched("00:00", "00:00")
        assert _is_in_sleep_window(s, _utc(*MON, 0, 0)) is False
        assert _is_in_sleep_window(s, _utc(*MON, 12, 0)) is False

    def test_near_full_day_overnight_window(self):
        """
        00:01→00:00 is an overnight window covering almost 24 hours.
        Every time from 00:01 through 23:59 falls inside; only 00:00 is outside.
        """
        s = _sched("00:01", "00:00")
        assert _is_in_sleep_window(s, _utc(*MON, 0, 1)) is True, "00:01 (start, inclusive)"
        assert _is_in_sleep_window(s, _utc(*MON, 12, 0)) is True, "midday"
        assert _is_in_sleep_window(s, _utc(*MON, 23, 59)) is True, "23:59"
        assert _is_in_sleep_window(s, _utc(*MON, 0, 0)) is False, "00:00 (exclusive end)"
