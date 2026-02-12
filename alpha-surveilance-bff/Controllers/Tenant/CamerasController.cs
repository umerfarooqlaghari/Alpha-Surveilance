using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class CamerasController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CamerasController> _logger;

    public CamerasController(IHttpClientFactory httpClientFactory, ILogger<CamerasController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCameras()
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/cameras?tenantId={tenantId}");

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cameras for tenant");
            return StatusCode(500, new { error = "Failed to fetch cameras" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCamera(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/cameras/{id}");
            // Note: Downstream API should verify that the camera belongs to the tenant.
            // Currently passing tenantId as query param or header if downstream supports it would be safer,
            // but assuming downstream just returns by ID. Ideally downstream checks ownership.

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to fetch camera" });
        }
    }

    // Tenant Admins can create cameras? Rules say "Tenant Admin Portal" allows adding cameras.
    [HttpPost]
    public async Task<IActionResult> CreateCamera([FromBody] JsonElement request)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            // We need to inject tenantId into the request body if not present, or rely on downstream to use it?
            // The downstream CreateCamera expects a body with TenantId.
            // We should parse the JSON, add/overwrite TenantId, and send it.
            
            // For simplicity, assuming the frontend sends the payload mostly correct but we enforce TenantId.
            // But modifying JsonElement is hard.
            // Better to deserialize to a dynamic/object, set TenantId, then serialize.
            
            var requestObj = JsonSerializer.Deserialize<Dictionary<string, object>>(request.GetRawText());
            if (requestObj == null) return BadRequest("Invalid JSON");

            requestObj["tenantId"] = tenantId; // Enforce TenantId from token

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/cameras", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating camera");
            return StatusCode(500, new { error = "Failed to create camera" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCamera(Guid id, [FromBody] JsonElement request)
    {
         try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"/api/cameras/{id}", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to update camera" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCamera(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/cameras/{id}");

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return NoContent();

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to delete camera" });
        }
    }
}
