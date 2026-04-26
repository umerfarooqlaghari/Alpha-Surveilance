namespace violation_management_api.DTOs.Requests;

public class CreateUserRequest
{
    public Guid? TenantId { get; set; } // Null for SuperAdmin
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
    public string Password { get; set; } = string.Empty;
    public List<Guid> RoleIds { get; set; } = new();
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
