namespace violation_management_api.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; } // Nullable for SuperAdmin users
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty; // Unique
    public string PhoneNumber { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
    public string PasswordHash { get; set; } = string.Empty; // BCrypt hashed
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
    
    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
