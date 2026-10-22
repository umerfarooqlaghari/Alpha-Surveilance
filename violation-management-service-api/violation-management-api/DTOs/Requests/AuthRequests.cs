namespace violation_management_api.DTOs.Requests;

public class SuperAdminLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TurnstileToken { get; set; }
}

public class TenantAdminLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string? TurnstileToken { get; set; }
}

public class TenantSignupRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Industry { get; set; }
    public int EmployeeCount { get; set; } = 1;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TurnstileToken { get; set; }
}

public class ValidateTokenRequest
{
    public string Token { get; set; } = string.Empty;
}

