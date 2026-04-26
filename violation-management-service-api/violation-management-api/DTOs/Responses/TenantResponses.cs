using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class TenantResponse
{
    public Guid Id { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public int CameraCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static TenantResponse FromEntity(Tenant tenant)
    {
        return new TenantResponse
        {
            Id = tenant.Id,
            TenantName = tenant.TenantName,
            Slug = tenant.Slug,
            EmployeeCount = tenant.EmployeeCount,
            Address = tenant.Address,
            City = tenant.City,
            Country = tenant.Country,
            LogoUrl = tenant.LogoUrl,
            Status = tenant.Status.ToString(),
            Industry = tenant.Industry,
            UserCount = tenant.Users?.Count ?? 0,
            CameraCount = tenant.Cameras?.Count ?? 0,
            CreatedAt = tenant.CreatedAt,
            UpdatedAt = tenant.UpdatedAt
        };
    }
}

public class TenantListResponse
{
    public List<TenantResponse> Tenants { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
