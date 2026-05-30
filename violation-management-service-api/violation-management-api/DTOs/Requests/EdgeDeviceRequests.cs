using System.ComponentModel.DataAnnotations;

namespace violation_management_api.DTOs.Requests;

/// <summary>
/// Idempotent registration call from the Vision Inference Service on startup.
/// Called via the internal API key endpoint /api/devices/internal/register.
/// </summary>
public class RegisterDeviceRequest
{
    /// <summary>Stable per-device identifier (UUID file, env var, or MAC fallback).</summary>
    [Required]
    [StringLength(128)]
    public string DeviceIdentifier { get; set; } = string.Empty;

    [Required]
    public Guid TenantId { get; set; }

    [StringLength(255)]
    public string Hostname { get; set; } = string.Empty;

    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// SuperAdmin-side creation of a device (when the admin pre-provisions one
/// before the physical device boots).
/// </summary>
public class CreateEdgeDeviceRequest
{
    [Required]
    public Guid TenantId { get; set; }

    public Guid? LocationId { get; set; }

    [Required]
    [StringLength(128)]
    public string DeviceIdentifier { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(255)]
    public string Hostname { get; set; } = string.Empty;
}

public class UpdateEdgeDeviceRequest
{
    [StringLength(200)]
    public string? DisplayName { get; set; }

    [StringLength(255)]
    public string? Hostname { get; set; }

    public Guid? LocationId { get; set; }

    /// <summary>0 = Active, 1 = Disabled</summary>
    public int? Status { get; set; }
}

public class AssignCameraToDeviceRequest
{
    [Required]
    public Guid CameraId { get; set; }
}
