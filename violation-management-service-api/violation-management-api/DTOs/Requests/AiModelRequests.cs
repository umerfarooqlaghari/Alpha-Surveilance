using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Requests;

public class RegisterAiModelRequest
{
    public string  ModelKey     { get; set; } = string.Empty;
    public string  DisplayName  { get; set; } = string.Empty;
    public string  Description  { get; set; } = string.Empty;
    public AiModelType ModelType { get; set; } = AiModelType.YoloLocal;

    public string? DownloadUrl  { get; set; }
    public string? S3Bucket     { get; set; }
    public string? S3Key        { get; set; }
    public string? LocalPath    { get; set; }
    public string? Version      { get; set; }
    public string? Sha256Checksum { get; set; }
}

/// <summary>Posted by the edge device after a download attempt.</summary>
public class EdgeModelStatusUpdate
{
    /// <summary>"Available" | "Downloading" | "Error"</summary>
    public string  Status        { get; set; } = string.Empty;
    public string? ErrorMessage  { get; set; }
    public string? Sha256Checksum { get; set; }
    public long?   FileSizeBytes { get; set; }
}
