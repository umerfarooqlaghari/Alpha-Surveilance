namespace AlphaSurveilance.DTO.Requests;

public class ViolationPayload
{
    public string TenantId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? FramePath { get; set; } = string.Empty; 
    public string CorrelationId { get; set; } = string.Empty; 
    public string? CameraId { get; set; }
    public string? ModelIdentifier { get; set; }
    public string? MetadataJson { get; set; }
    public Guid? EmployeeId { get; set; }
}