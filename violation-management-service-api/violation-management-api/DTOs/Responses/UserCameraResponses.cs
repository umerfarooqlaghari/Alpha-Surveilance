using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class UserResponse
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public static UserResponse FromEntity(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            TenantName = user.Tenant?.TenantName,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            EmployeeCode = user.EmployeeCode,
            Designation = user.Designation,
            IsActive = user.IsActive,
            Roles = user.UserRoles?.Select(ur => ur.Role.Name).ToList() ?? new(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}

public class CameraResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool EnableSafetyViolations { get; set; }
    public bool EnableSecurityViolations { get; set; }
    public bool EnableOperationalViolations { get; set; }
    public bool EnableComplianceViolations { get; set; }
    public DateTime CreatedAt { get; set; }
    // Note: RTSP URL is NOT exposed for security

    public static CameraResponse FromEntity(Camera camera)
    {
        return new CameraResponse
        {
            Id = camera.Id,
            TenantId = camera.TenantId,
            TenantName = camera.Tenant?.TenantName,
            CameraId = camera.CameraId,
            Name = camera.Name,
            Location = camera.Location,
            Status = camera.Status.ToString(),
            EnableSafetyViolations = camera.EnableSafetyViolations,
            EnableSecurityViolations = camera.EnableSecurityViolations,
            EnableOperationalViolations = camera.EnableOperationalViolations,
            EnableComplianceViolations = camera.EnableComplianceViolations,
            CreatedAt = camera.CreatedAt
        };
    }
}
