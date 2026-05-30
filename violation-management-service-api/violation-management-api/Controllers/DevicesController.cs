using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using AlphaSurveilance.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController(
    IEdgeDeviceService deviceService,
    ILogger<DevicesController> logger,
    ICurrentTenantService currentTenantService) : ControllerBase
{
    // ════════════════════════════════════════════════════════════════════════
    // SERVICE-TO-SERVICE — protected by InternalApiKeyMiddleware
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Idempotent device registration called by the Vision Inference Service
    /// on startup. Returns the device's assigned UUID so the service can scope
    /// subsequent /api/cameras/internal/active calls.
    /// </summary>
    [HttpPost("internal/register")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request)
    {
        try
        {
            var (device, isNew) = await deviceService.RegisterAsync(request);
            return Ok(new RegisterDeviceResponse
            {
                DeviceId = device.Id,
                DeviceIdentifier = device.DeviceIdentifier,
                Status = device.Status,
                IsNew = isNew
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Internal] Error registering device");
            return StatusCode(500, new { error = "Failed to register device" });
        }
    }

    /// <summary>
    /// Heartbeat called by the vision service on each camera-poll cycle.
    /// Updates LastSeenAt so the SuperAdmin UI can render online status.
    /// </summary>
    [HttpPatch("internal/{id}/heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat(Guid id)
    {
        var ok = await deviceService.RecordHeartbeatAsync(id);
        if (!ok) return NotFound(new { error = "Device not found" });
        return NoContent();
    }

    // ════════════════════════════════════════════════════════════════════════
    // SUPER-ADMIN — protected by JWT
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateEdgeDeviceRequest request)
    {
        try
        {
            var device = await deviceService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = device.Id }, device);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating edge device");
            return StatusCode(500, new { error = "Failed to create device" });
        }
    }

    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? tenantId)
    {
        try
        {
            var devices = tenantId.HasValue
                ? await deviceService.GetByTenantAsync(tenantId.Value)
                : await deviceService.GetAllAsync();
            return Ok(devices);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing edge devices");
            return StatusCode(500, new { error = "Failed to list devices" });
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var device = await deviceService.GetByIdAsync(id);
        if (device == null) return NotFound();
        return Ok(device);
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEdgeDeviceRequest request)
    {
        try
        {
            var updated = await deviceService.UpdateAsync(id, request);
            if (updated == null) return NotFound();
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating edge device {DeviceId}", id);
            return StatusCode(500, new { error = "Failed to update device" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ok = await deviceService.DeleteAsync(id);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpPost("{deviceId}/cameras/{cameraId}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> AssignCamera(Guid deviceId, Guid cameraId)
    {
        try
        {
            var ok = await deviceService.AssignCameraAsync(deviceId, cameraId);
            if (!ok) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{deviceId}/cameras/{cameraId}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UnassignCamera(Guid deviceId, Guid cameraId)
    {
        var ok = await deviceService.UnassignCameraAsync(deviceId, cameraId);
        if (!ok) return NotFound();
        return NoContent();
    }
}
