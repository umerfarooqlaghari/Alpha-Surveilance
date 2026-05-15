using System;

namespace violation_management_api.Core.Entities;

public class TenantSidebarModule
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Unique key for the module (e.g., "Heatmaps", "Planograms", "ConstructionSafety").
    /// </summary>
    public string ModuleKey { get; set; } = string.Empty;
    
    public bool IsEnabled { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
}
