using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class ViolationsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ViolationsController> _logger;

    public ViolationsController(IHttpClientFactory httpClientFactory, ILogger<ViolationsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetViolations()
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            
            // Downstream API expects X-Tenant-Id header for getting violations
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/violations");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching violations for tenant");
            return StatusCode(500, new { error = "Failed to fetch violations" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetViolation(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/violations/{id}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching violation {ViolationId}", id);
            return StatusCode(500, new { error = "Failed to fetch violation" });
        }
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics([FromQuery] string? startDate, [FromQuery] string? endDate, [FromQuery] string? cameraId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            if (!string.IsNullOrEmpty(startDate)) query["startDate"] = startDate;
            if (!string.IsNullOrEmpty(endDate)) query["endDate"] = endDate;
            if (!string.IsNullOrEmpty(cameraId)) query["cameraId"] = cameraId;

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/violations/analytics?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                 return StatusCode((int)response.StatusCode, responseContent);
            }
            
            return Content(responseContent, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching analytics");
            return StatusCode(500, new { error = "Failed to fetch analytics" });
        }
    }
}
