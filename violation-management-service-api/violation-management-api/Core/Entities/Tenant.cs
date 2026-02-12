using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty; // URL-friendly identifier
    public int EmployeeCount { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? LogoUrl { get; set; } // Cloudinary URL
    public string? LogoPublicId { get; set; } // Cloudinary public ID for deletion
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string Industry { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Camera> Cameras { get; set; } = new List<Camera>();
    public ICollection<Violation> Violations { get; set; } = new List<Violation>();
}

public enum TenantStatus
{
    Active = 0,
    Inactive = 1,
    Suspended = 2
}
