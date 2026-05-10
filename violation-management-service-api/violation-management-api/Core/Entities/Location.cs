using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

public class Location
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Display name (e.g. "HQ — North Wing", "Karachi Plant").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short, unique-per-tenant code (e.g. "HQ-N", "KHI01").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Timezone { get; set; }

    public LocationStatus Status { get; set; } = LocationStatus.Active;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Camera> Cameras { get; set; } = new List<Camera>();
    public ICollection<Violation> Violations { get; set; } = new List<Violation>();
}

public enum LocationStatus
{
    Active = 0,
    Inactive = 1
}
