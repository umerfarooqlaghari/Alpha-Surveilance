using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;

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

    /// <summary>
    /// Optional FK to the on-premise EdgeDevice that serves this camera's
    /// RTSP stream. When NULL the camera is in the tenant's "shared pool":
    /// every active device for the tenant will pick it up. When set, only
    /// that specific device will stream and run inference for the camera.
    /// Nullable to preserve backward compatibility with single-device
    /// deployments that pre-date the EdgeDevice table.
    /// </summary>
    public Guid? DeviceId { get; set; }
    public EdgeDevice? Device { get; set; }

    public string CameraId { get; set; } = string.Empty; // Unique identifier
    public string Name { get; set; } = string.Empty; // Friendly name
    public string Location { get; set; } = string.Empty; // Physical location (free-text descriptor; deprecated in favour of LocationRef)
    public string RtspUrlEncrypted { get; set; } = string.Empty; // AES encrypted RTSP URL
    public CameraStatus Status { get; set; } = CameraStatus.Active;

    /// <summary>
    /// Configures whether camera is used for Attendance Marking:
    /// None (0), MarkIn (1), MarkOut (2), Bidirectional (3).
    /// </summary>
    public AttendanceMode AttendanceMode { get; set; } = AttendanceMode.None;

    
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
    // Note: Violation.CameraId is a denormalized string identifier, NOT a FK to this table.
    // The navigation is intentionally omitted to prevent EF Core from creating a shadow FK (CameraId1).
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
