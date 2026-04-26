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
    public string RtspUrl { get; set; } = string.Empty;    // DECRYPTED — internal only!
    public string WhipUrl { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public double TargetFps { get; set; } = 1.0;
    public List<ViolationRuleDto> ViolationRules { get; set; } = new();
}

public class ViolationRuleDto
{
    public Guid SopViolationTypeId { get; set; }
    public string ModelIdentifier { get; set; } = string.Empty;
    public string TriggerLabels { get; set; } = string.Empty;
}
