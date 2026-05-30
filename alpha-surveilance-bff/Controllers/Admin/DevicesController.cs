using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
public class DevicesController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(IHttpClientFactory httpClientFactory, ILogger<DevicesController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/devices", content);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating device");
            return StatusCode(500, new { error = "Failed to create device" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? tenantId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var qs = tenantId.HasValue ? $"?tenantId={tenantId}" : string.Empty;
            var response = await client.GetAsync($"/api/devices{qs}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing devices");
            return StatusCode(500, new { error = "Failed to list devices" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/devices/{id}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching device {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch device" });
        }
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PatchAsync($"/api/devices/{id}", content);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating device {Id}", id);
            return StatusCode(500, new { error = "Failed to update device" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/devices/{id}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device {Id}", id);
            return StatusCode(500, new { error = "Failed to delete device" });
        }
    }

    [HttpPost("{deviceId}/cameras/{cameraId}")]
    public async Task<IActionResult> AssignCamera(Guid deviceId, Guid cameraId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.PostAsync($"/api/devices/{deviceId}/cameras/{cameraId}", null);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning camera {CameraId} to device {DeviceId}", cameraId, deviceId);
            return StatusCode(500, new { error = "Failed to assign camera" });
        }
    }

    [HttpDelete("{deviceId}/cameras/{cameraId}")]
    public async Task<IActionResult> UnassignCamera(Guid deviceId, Guid cameraId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/devices/{deviceId}/cameras/{cameraId}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing camera {CameraId} from device {DeviceId}", cameraId, deviceId);
            return StatusCode(500, new { error = "Failed to unassign camera" });
        }
    }
}
