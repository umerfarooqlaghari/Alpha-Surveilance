namespace violation_management_api.Core.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // e.g., "tenants.create", "cameras.read"
    public string Resource { get; set; } = string.Empty; // e.g., "tenants", "cameras", "violations"
    public string Action { get; set; } = string.Empty; // e.g., "create", "read", "update", "delete"
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
