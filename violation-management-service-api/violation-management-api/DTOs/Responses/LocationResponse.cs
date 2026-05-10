using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class LocationResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Timezone { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CameraCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static LocationResponse FromEntity(Location location, int? cameraCount = null)
    {
        return new LocationResponse
        {
            Id = location.Id,
            TenantId = location.TenantId,
            TenantName = location.Tenant?.TenantName,
            Name = location.Name,
            Code = location.Code,
            Address = location.Address,
            City = location.City,
            Country = location.Country,
            Timezone = location.Timezone,
            Status = location.Status.ToString(),
            CameraCount = cameraCount ?? location.Cameras?.Count ?? 0,
            CreatedAt = location.CreatedAt,
            UpdatedAt = location.UpdatedAt
        };
    }
}
