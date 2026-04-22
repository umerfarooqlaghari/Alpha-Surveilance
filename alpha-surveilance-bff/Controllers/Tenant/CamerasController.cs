using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class CamerasController : ProxyControllerBase
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/cameras?tenantId={tenantId}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/cameras/{id}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to fetch camera" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateCamera([FromBody] JsonElement request)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            // Inject tenantId into the request body — enforce it from the JWT claim
            var requestObj = JsonSerializer.Deserialize<Dictionary<string, object>>(request.GetRawText());
            if (requestObj == null) return BadRequest("Invalid JSON");
            requestObj["tenantId"] = tenantId;

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cameras")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating camera");
            return StatusCode(500, new { error = "Failed to create camera" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCamera(Guid id, [FromBody] JsonElement body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var httpRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/cameras/{id}")
            {
                Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);
            return await ProxyResponse(response);
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
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/cameras/{id}");
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return NoContent();

            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting camera {CameraId}", id);
            return StatusCode(500, new { error = "Failed to delete camera" });
        }
    }
}
