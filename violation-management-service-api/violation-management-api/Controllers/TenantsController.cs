using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(ITenantService tenantService, ILogger<TenantsController> logger)
    {
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new tenant
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")] // TODO: Add authorization
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        try
        {
            var tenant = await _tenantService.CreateTenantAsync(request);
            return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return StatusCode(500, new { error = "An error occurred while creating the tenant" });
        }
    }

    /// <summary>
    /// Get all tenants with pagination
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAllTenants([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await _tenantService.GetAllTenantsAsync(pageNumber, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tenants");
            return StatusCode(500, new { error = "An error occurred while fetching tenants" });
        }
    }

    /// <summary>
    /// Get tenant by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        try
        {
            var tenant = await _tenantService.GetTenantByIdAsync(id);
            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tenant {TenantId}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the tenant" });
        }
    }

    /// <summary>
    /// Get tenant by slug
    /// </summary>
    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetTenantBySlug(string slug)
    {
        try
        {
            var tenant = await _tenantService.GetTenantBySlugAsync(slug);
            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tenant by slug {Slug}", slug);
            return StatusCode(500, new { error = "An error occurred while fetching the tenant" });
        }
    }

    /// <summary>
    /// Update tenant
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request)
    {
        try
        {
            var tenant = await _tenantService.UpdateTenantAsync(id, request);
            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant {TenantId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the tenant" });
        }
    }

    /// <summary>
    /// Update tenant status (Activate/Deactivate/Suspend)
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateTenantStatus(Guid id, [FromBody] UpdateTenantStatusRequest request)
    {
        try
        {
            if (!Enum.IsDefined(typeof(TenantStatus), request.Status))
                return BadRequest(new { error = "Invalid status value" });

            var tenant = await _tenantService.UpdateTenantStatusAsync(id, (TenantStatus)request.Status);
            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant status {TenantId}", id);
            return StatusCode(500, new { error = "An error occurred while updating tenant status" });
        }
    }

    /// <summary>
    /// Upload tenant logo
    /// </summary>
    [HttpPost("{id}/logo")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> UploadLogo(Guid id, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            var result = await _tenantService.UploadTenantLogoAsync(id, file);
            if (result == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(new { logoUrl = result.Value.Url, publicId = result.Value.PublicId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading logo for tenant {TenantId}", id);
            return StatusCode(500, new { error = "An error occurred while uploading the logo" });
        }
    }

    /// <summary>
    /// Soft delete tenant
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DeleteTenant(Guid id)
    {
        try
        {
            var result = await _tenantService.DeleteTenantAsync(id);
            if (!result)
                return NotFound(new { error = "Tenant not found" });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant {TenantId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the tenant" });
        }
    }
}
