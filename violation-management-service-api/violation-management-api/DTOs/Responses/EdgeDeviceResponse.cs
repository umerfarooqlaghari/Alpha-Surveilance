using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class EdgeDeviceResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string DeviceIdentifier { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastSeenAt { get; set; }
    public int CameraCount { get; set; }
    public List<Guid> DistinctLocationIds { get; set; } = new();
    public DateTime RegisteredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static EdgeDeviceResponse FromEntity(
        EdgeDevice device,
        int? cameraCount = null,
        List<Guid>? distinctLocationIds = null)
    {
        return new EdgeDeviceResponse
        {
            Id = device.Id,
            TenantId = device.TenantId,
            TenantName = device.Tenant?.TenantName,
            LocationId = device.LocationId,
            LocationName = device.LocationRef?.Name,
            DeviceIdentifier = device.DeviceIdentifier,
            Hostname = device.Hostname,
            DisplayName = device.DisplayName,
            Status = device.Status.ToString(),
            LastSeenAt = device.LastSeenAt,
            CameraCount = cameraCount ?? device.Cameras?.Count ?? 0,
            DistinctLocationIds = distinctLocationIds ?? new List<Guid>(),
            RegisteredAt = device.RegisteredAt,
            UpdatedAt = device.UpdatedAt
        };
    }
}

/// <summary>
/// Compact response returned from the internal registration endpoint —
/// the vision service only needs the assigned device Id.
/// </summary>
public class RegisterDeviceResponse
{
    public Guid DeviceId { get; set; }
    public string DeviceIdentifier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsNew { get; set; }
}
