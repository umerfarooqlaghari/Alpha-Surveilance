namespace AlphaSurveilance.DTO.Requests;

public class ViolationPayload
{
    public string TenantId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? FramePath { get; set; } = string.Empty; 
    public string CorrelationId { get; set; } = string.Empty; 
    public string? CameraId { get; set; }
    public string Severity { get; set; } = "Low"; // Default to Low if missing
    public string? MetadataJson { get; set; }
}