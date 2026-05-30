namespace violation_management_api.DTOs.Responses;

public class AiModelResponse
{
    public Guid      Id            { get; set; }
    public string    ModelKey      { get; set; } = string.Empty;
    public string    DisplayName   { get; set; } = string.Empty;
    public string    Description   { get; set; } = string.Empty;
    public string    ModelType     { get; set; } = string.Empty;
    public string    Status        { get; set; } = string.Empty;

    public string?   DownloadUrl   { get; set; }
    public string?   S3Bucket      { get; set; }
    public string?   S3Key         { get; set; }
    public string?   LocalPath     { get; set; }

    public string?   Version       { get; set; }
    public long?     FileSizeBytes { get; set; }
    public string?   Sha256Checksum { get; set; }
    public string?   ErrorMessage  { get; set; }
    public DateTime? DownloadedAt  { get; set; }

    /// <summary>Number of SopViolationTypes currently pointing at this model.</summary>
    public int       SopRuleCount  { get; set; }

    public DateTime  CreatedAt     { get; set; }
    public DateTime? UpdatedAt     { get; set; }
}
