using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

/// <summary>
/// On-premise vision inference device (physical or virtual host) that serves
/// a subset of a tenant's cameras. Multiple devices can split a tenant's
/// camera pool when one device cannot keep up with all streams.
///
/// Identification is a stable per-device string captured on first boot:
/// either an explicit DEVICE_ID env var (preferred for orchestrated
/// deployments) or a generated UUID persisted to a local file. MAC address
/// is used only as a last-resort fallback because container runtimes
/// randomise it on each restart.
/// </summary>
public class EdgeDevice
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional primary location hint. Cameras attached to this device do not
    /// need to belong to this location, but the UI uses it to warn admins
    /// when they assign cameras from a different location.
    /// </summary>
    public Guid? LocationId { get; set; }
    public Location? LocationRef { get; set; }

    /// <summary>
    /// Stable identifier reported by the vision service on registration.
    /// Unique per tenant.
    /// </summary>
    public string DeviceIdentifier { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Human-friendly label shown in the SuperAdmin dashboard
    /// e.g. "Kitchen Floor Device" or "Production Line 2".
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public EdgeDeviceStatus Status { get; set; } = EdgeDeviceStatus.Active;

    /// <summary>
    /// Updated by the vision service on every camera-poll cycle. The UI
    /// renders the device as Online / Idle / Offline based on this.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Camera> Cameras { get; set; } = new List<Camera>();
}

public enum EdgeDeviceStatus
{
    Active = 0,
    Disabled = 1
}
