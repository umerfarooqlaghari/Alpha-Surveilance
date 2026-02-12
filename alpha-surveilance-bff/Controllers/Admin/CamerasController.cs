using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
public class CamerasController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CamerasController> _logger;

    public CamerasController(IHttpClientFactory httpClientFactory, ILogger<CamerasController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCamera([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
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

    [HttpGet]
    public async Task<IActionResult> GetCameras([FromQuery] Guid tenantId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/cameras?tenantId={tenantId}");

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cameras");
            return StatusCode(500, new { error = "Failed to fetch cameras" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCamera(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/cameras/{id}");

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to fetch camera" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCamera(Guid id, [FromBody] JsonElement request)
    {
        try
        {
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

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateCameraStatus(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/cameras/{id}/status")
            {
                Content = content
            };
            var response = await client.SendAsync(httpRequest);

            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera status {CameraId}", id);
            return StatusCode(500, new { error = "Failed to update camera status" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCamera(Guid id)
    {
        try
        {
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
