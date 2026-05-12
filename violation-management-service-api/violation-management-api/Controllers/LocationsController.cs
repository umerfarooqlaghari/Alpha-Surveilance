using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;
using AlphaSurveilance.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocationsController(
    ILocationService locationService,
    ILogger<LocationsController> logger,
    ICurrentTenantService currentTenantService) : ControllerBase
{
    private Guid GetTenantId()
    {
        var tenantId = currentTenantService.TenantId;
        if (!tenantId.HasValue)
            throw new UnauthorizedAccessException("User is not associated with a tenant.");
        return tenantId.Value;
    }

    [HttpPost]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> CreateLocation([FromBody] CreateLocationRequest request)
    {
        try
        {
            if (currentTenantService.IsSuperAdmin)
            {
                if (request.TenantId == Guid.Empty)
                    return BadRequest(new { error = "Tenant ID is required for Super Admin when creating a location." });
            }
            else
            {
                request.TenantId = GetTenantId();
            }

            var location = await locationService.CreateLocationAsync(request);
            return CreatedAtAction(nameof(GetLocation), new { id = location.Id }, location);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating location");
            return StatusCode(500, new { error = "An error occurred while creating the location" });
        }
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetLocations(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? search = null)
    {
        try
        {
            Guid targetTenantId;
            if (currentTenantService.IsSuperAdmin)
            {
                if (!tenantId.HasValue) return BadRequest(new { error = "Tenant ID is required for Super Admin" });
                targetTenantId = tenantId.Value;
            }
            else
            {
                targetTenantId = GetTenantId();
            }

            var locations = await locationService.GetLocationsByTenantAsync(targetTenantId, search);
            return Ok(locations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching locations");
            return StatusCode(500, new { error = "An error occurred while fetching locations" });
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> GetLocation(Guid id)
    {
        try
        {
            var location = await locationService.GetLocationByIdAsync(id);
            if (location == null) return NotFound(new { error = "Location not found" });

            if (!currentTenantService.IsSuperAdmin && location.TenantId != GetTenantId())
                return Forbid();

            return Ok(location);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching location {LocationId}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the location" });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest request)
    {
        try
        {
            if (!currentTenantService.IsSuperAdmin)
            {
                var existing = await locationService.GetLocationByIdAsync(id);
                if (existing == null) return NotFound();
                if (existing.TenantId != GetTenantId()) return Forbid();
            }

            var location = await locationService.UpdateLocationAsync(id, request);
            if (location == null) return NotFound(new { error = "Location not found" });
            return Ok(location);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating location {LocationId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the location" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> DeleteLocation(Guid id)
    {
        try
        {
            if (!currentTenantService.IsSuperAdmin)
            {
                var existing = await locationService.GetLocationByIdAsync(id);
                if (existing == null) return NotFound();
                if (existing.TenantId != GetTenantId()) return Forbid();
            }

            var ok = await locationService.DeleteLocationAsync(id);
            if (!ok) return NotFound(new { error = "Location not found" });
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting location {LocationId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the location" });
        }
    }
}
