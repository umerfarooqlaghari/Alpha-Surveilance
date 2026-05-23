using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

public class Camera
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional structured Location (sub-tenant) this camera belongs to.
    /// Nullable for backward compatibility; may be enforced non-null in a future phase.
    /// </summary>
    public Guid? LocationId { get; set; }
    public Location? LocationRef { get; set; }

    public string CameraId { get; set; } = string.Empty; // Unique identifier
    public string Name { get; set; } = string.Empty; // Friendly name
    public string Location { get; set; } = string.Empty; // Physical location (free-text descriptor; deprecated in favour of LocationRef)
    public string RtspUrlEncrypted { get; set; } = string.Empty; // AES encrypted RTSP URL
    public CameraStatus Status { get; set; } = CameraStatus.Active;
    
    // WebRTC Streaming Fields
    public string WhipUrl { get; set; } = string.Empty;
    public string WhepUrl { get; set; } = string.Empty;
    public string CloudflareUid { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public double TargetFps { get; set; } = 1.0;

    /// <summary>
    /// Detection kill-switch. When false the Vision Inference Service will not
    /// open an RTSP connection to this camera at all (no decode, no inference,
    /// no violations). Equivalent to "putting the camera to sleep" without
    /// deleting it or its rule configuration.
    /// Defaults to true so existing cameras are unaffected by the migration.
    /// </summary>
    public bool IsDetectionEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    
    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Violation> Violations { get; set; } = new List<Violation>();
    public ICollection<CameraViolationType> ActiveViolationTypes { get; set; } = new List<CameraViolationType>();
    public ICollection<DetectionSchedule> DetectionSchedules { get; set; } = new List<DetectionSchedule>();
}

public enum CameraStatus
{
    Active = 0,
    Inactive = 1,
    Maintenance = 2,
    Error = 3
}
