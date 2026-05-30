namespace violation_management_api.DTOs.Responses;

/// <summary>
/// Returned by the internal /api/cameras/internal/active endpoint.
/// Contains decrypted RTSP URLs and service flags for the Vision Inference Service.
/// This DTO is NEVER exposed to end-user clients.
/// </summary>
public class InternalCameraDto
{
    public Guid Id { get; set; }                    // DB UUID (for referencing in violations)
    public string CameraId { get; set; } = string.Empty;   // Friendly slug e.g. "CAM-GATE-01"
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? LocationCode { get; set; }
    public string RtspUrl { get; set; } = string.Empty;    // DECRYPTED — internal only!
    public string WhipUrl { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public double TargetFps { get; set; } = 1.0;
    /// <summary>When false the Vision Service must not open an RTSP connection for this camera.</summary>
    public bool IsDetectionEnabled { get; set; } = true;
    /// <summary>Recurring sleep windows. Vision Service skips inference when inside any active window.</summary>
    public List<DetectionScheduleDto> DetectionSchedules { get; set; } = new();
    public List<ViolationRuleDto> ViolationRules { get; set; } = new();
}

public class ViolationRuleDto
{
    public Guid SopViolationTypeId { get; set; }
    public string ModelIdentifier { get; set; } = string.Empty;
    public string TriggerLabels { get; set; } = string.Empty;
    public string? RuleConfigurationJson { get; set; }

    // ── Model registry metadata (from AiModel) ────────────────────────────
    /// <summary>"Available" | "Disabled" | "Registered" | "Error" — inference service honours this.</summary>
    public string  ModelStatus      { get; set; } = "Available";
    /// <summary>"YoloLocal" | "YoloFineTuned" | "RoboflowCloud"</summary>
    public string  ModelType        { get; set; } = "YoloLocal";
    public string? ModelDownloadUrl { get; set; }
    public string? ModelS3Bucket    { get; set; }
    public string? ModelS3Key       { get; set; }
    public string? ModelLocalPath   { get; set; }
    public string? ModelSha256      { get; set; }
    public Guid?   AiModelId        { get; set; }
}
