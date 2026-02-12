using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CamerasController : ControllerBase
{
    private readonly ICameraService _cameraService;
    private readonly ILogger<CamerasController> _logger;

    public CamerasController(ICameraService cameraService, ILogger<CamerasController> logger)
    {
        _cameraService = cameraService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new camera for a tenant
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> CreateCamera([FromBody] CreateCameraRequest request)
    {
        try
        {
            var camera = await _cameraService.CreateCameraAsync(request);
            return CreatedAtAction(nameof(GetCamera), new { id = camera.Id }, camera);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating camera");
            return StatusCode(500, new { error = "An error occurred while creating the camera" });
        }
    }

    /// <summary>
    /// Get all cameras for a tenant
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> GetCamerasByTenant([FromQuery] Guid tenantId)
    {
        try
        {
            var cameras = await _cameraService.GetCamerasByTenantAsync(tenantId);
            return Ok(cameras);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cameras for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "An error occurred while fetching cameras" });
        }
    }

    /// <summary>
    /// Get camera by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> GetCamera(Guid id)
    {
        try
        {
            var camera = await _cameraService.GetCameraByIdAsync(id);
            if (camera == null)
                return NotFound(new { error = "Camera not found" });

            return Ok(camera);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching camera {CameraId}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the camera" });
        }
    }

    /// <summary>
    /// Get decrypted RTSP URL for a camera (internal use only)
    /// </summary>
    [HttpGet("{id}/rtsp-url")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetRtspUrl(Guid id)
    {
        try
        {
            var rtspUrl = await _cameraService.GetDecryptedRtspUrlAsync(id);
            if (rtspUrl == null)
                return NotFound(new { error = "Camera not found" });

            return Ok(new { rtspUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching RTSP URL for camera {CameraId}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the RTSP URL" });
        }
    }

    /// <summary>
    /// Update camera
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> UpdateCamera(Guid id, [FromBody] UpdateCameraRequest request)
    {
        try
        {
            var camera = await _cameraService.UpdateCameraAsync(id, request);
            if (camera == null)
                return NotFound(new { error = "Camera not found" });

            return Ok(camera);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera {CameraId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the camera" });
        }
    }

    /// <summary>
    /// Update camera status
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> UpdateCameraStatus(Guid id, [FromBody] UpdateCameraStatusRequest request)
    {
        try
        {
            if (!Enum.IsDefined(typeof(CameraStatus), request.Status))
                return BadRequest(new { error = "Invalid status value" });

            var camera = await _cameraService.UpdateCameraStatusAsync(id, (CameraStatus)request.Status);
            if (camera == null)
                return NotFound(new { error = "Camera not found" });

            return Ok(camera);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera status {CameraId}", id);
            return StatusCode(500, new { error = "An error occurred while updating camera status" });
        }
    }

    /// <summary>
    /// Delete camera
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DeleteCamera(Guid id)
    {
        try
        {
            var result = await _cameraService.DeleteCameraAsync(id);
            if (!result)
                return NotFound(new { error = "Camera not found" });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting camera {CameraId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the camera" });
        }
    }
}
