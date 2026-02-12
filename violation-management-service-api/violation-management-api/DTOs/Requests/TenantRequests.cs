namespace violation_management_api.DTOs.Requests;

public class CreateTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
}

public class UpdateTenantRequest
{
    public string? TenantName { get; set; }
    public int? EmployeeCount { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Industry { get; set; }
}

public class UpdateTenantStatusRequest
{
    public int Status { get; set; } // 0 = Active, 1 = Inactive, 2 = Suspended
}
