namespace violation_management_api.DTOs.Responses;

/// <summary>
/// Read-only representation of a DetectionSchedule sent to clients and to the
/// Vision Inference Service inside InternalCameraDto.
/// </summary>
public class DetectionScheduleDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// Bitmask — see <see cref="violation_management_api.Core.Entities.DetectionSchedule.DaysOfWeek"/>.
    /// 0 or 127 = every day.
    /// </summary>
    public int DaysOfWeek { get; set; }

    /// <summary>"HH:mm" in UTC, e.g. "22:00".</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>"HH:mm" in UTC, e.g. "06:00". May be less than StartTime for overnight windows.</summary>
    public string EndTime { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
