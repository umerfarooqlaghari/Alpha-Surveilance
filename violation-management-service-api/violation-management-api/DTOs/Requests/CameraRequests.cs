namespace violation_management_api.DTOs.Requests;

public class DetectionScheduleRequest
{
    public int DaysOfWeek { get; set; } = 127;       // 0 or 127 = every day
    public string StartTime { get; set; } = "00:00"; // "HH:mm" UTC
    public string EndTime { get; set; } = "00:00";   // "HH:mm" UTC
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class CameraViolationAssignment
{
    public Guid SopViolationTypeId { get; set; }
    public string? TriggerLabels { get; set; }
    /// <summary>
    /// Optional JSON-encoded policy configuration (geofence / anomaly / etc.).
    /// Validated by <see cref="violation_management_api.Services.RuleConfigurationValidator"/> on save.
    /// </summary>
    public string? RuleConfigurationJson { get; set; }
}

public class CreateCameraRequest
{
    public Guid TenantId { get; set; }
    /// <summary>Optional Location (sub-tenant) the camera belongs to. Must belong to the same tenant.</summary>
    public Guid? LocationId { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty; // Will be encrypted
    public string WhipUrl { get; set; } = string.Empty;
    public string WhepUrl { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public double TargetFps { get; set; } = 1.0;
    public bool IsDetectionEnabled { get; set; } = true;
    public List<CameraViolationAssignment> ActiveViolations { get; set; } = new();
    /// <summary>Recurring sleep windows. Replaces all schedules if provided.</summary>
    public List<DetectionScheduleRequest> DetectionSchedules { get; set; } = new();
}

public class UpdateCameraRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    /// <summary>
    /// Set to a Guid to assign / change the Location.
    /// Set to <see cref="Guid.Empty"/> to explicitly detach from any Location.
    /// Leave null to keep unchanged.
    /// </summary>
    public Guid? LocationId { get; set; }
    public string? RtspUrl { get; set; } // Will be encrypted if provided
    public string? WhipUrl { get; set; }
    public string? WhepUrl { get; set; }
    public bool? IsStreaming { get; set; }
    public double? TargetFps { get; set; }
    /// <summary>Null = leave unchanged. False = put camera to sleep (no RTSP, no inference).</summary>
    public bool? IsDetectionEnabled { get; set; }
    public List<CameraViolationAssignment>? ActiveViolations { get; set; }
    /// <summary>Null = leave unchanged. Empty list = delete all schedules.</summary>
    public List<DetectionScheduleRequest>? DetectionSchedules { get; set; }
}

public class UpdateCameraStatusRequest
{
    public int Status { get; set; } // 0=Active, 1=Inactive, 2=Maintenance, 3=Error
}
