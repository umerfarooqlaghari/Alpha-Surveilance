using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

public class Camera
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CameraId { get; set; } = string.Empty; // Unique identifier
    public string Name { get; set; } = string.Empty; // Friendly name
    public string Location { get; set; } = string.Empty; // Physical location
    public string RtspUrlEncrypted { get; set; } = string.Empty; // AES encrypted RTSP URL
    public CameraStatus Status { get; set; } = CameraStatus.Active;
    
    // WebRTC Streaming Fields
    public string WhipUrl { get; set; } = string.Empty;
    public string WhepUrl { get; set; } = string.Empty;
    public string CloudflareUid { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    
    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Violation> Violations { get; set; } = new List<Violation>();
    public ICollection<CameraViolationType> ActiveViolationTypes { get; set; } = new List<CameraViolationType>();
}

public enum CameraStatus
{
    Active = 0,
    Inactive = 1,
    Maintenance = 2,
    Error = 3
}
