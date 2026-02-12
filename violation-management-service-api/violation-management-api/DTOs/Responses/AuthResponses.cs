namespace violation_management_api.DTOs.Responses;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public UserInfo User { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public TenantInfo? Tenant { get; set; }
}

public class UserInfo
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Designation { get; set; }
}

public class TenantInfo
{
    public Guid Id { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}
