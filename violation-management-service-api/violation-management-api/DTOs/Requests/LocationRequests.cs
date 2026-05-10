using System.ComponentModel.DataAnnotations;

namespace violation_management_api.DTOs.Requests;

public class CreateLocationRequest
{
    /// <summary>
    /// Only honoured for SuperAdmin callers. For TenantAdmin the value is overridden by the JWT claim.
    /// </summary>
    public Guid TenantId { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Timezone { get; set; }
}

public class UpdateLocationRequest
{
    [StringLength(200)]
    public string? Name { get; set; }

    [StringLength(50)]
    public string? Code { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Timezone { get; set; }

    /// <summary>0 = Active, 1 = Inactive</summary>
    public int? Status { get; set; }
}
