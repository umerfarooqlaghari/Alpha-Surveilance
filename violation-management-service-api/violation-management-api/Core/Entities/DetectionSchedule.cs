namespace violation_management_api.Core.Entities;

/// <summary>
/// A recurring time-based sleep window for a camera's detection engine.
///
/// When the current UTC time falls inside any active DetectionSchedule that applies
/// to the current day, the camera is treated as if IsDetectionEnabled = false:
///   - The backend excludes it from GET /api/cameras/internal/active
///   - The Vision Inference Service skips inference on captured frames
///
/// DaysOfWeek bitmask (matches .NET DayOfWeek enum values):
///   Sunday  = 1  (1 << 0)
///   Monday  = 2  (1 << 1)
///   Tuesday = 4  (1 << 2)
///   Wednesday = 8 (1 << 3)
///   Thursday = 16 (1 << 4)
///   Friday  = 32 (1 << 5)
///   Saturday = 64 (1 << 6)
///   0 or 127 = every day
/// </summary>
public class DetectionSchedule
{
    public Guid Id { get; set; }
    public Guid CameraId { get; set; }
    public Camera Camera { get; set; } = null!;

    /// <summary>
    /// Bitmask of days this sleep window applies to.
    /// 0 or 127 = every day.
    /// </summary>
    public int DaysOfWeek { get; set; } = 127;

    /// <summary>Start of the sleep window in UTC (e.g. 22:00 = "22:00").</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// End of the sleep window in UTC.
    /// If EndTime &lt; StartTime the window spans midnight
    /// (e.g. 22:00 → 06:00 next day).
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Optional human-readable label, e.g. "Night quiet hours".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>When false this schedule entry is ignored without deleting it.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
