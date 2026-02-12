using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class DashboardController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IHttpClientFactory httpClientFactory, ILogger<DashboardController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/violations/stats");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard stats for tenant");
            return StatusCode(500, new { error = "Failed to fetch dashboard stats" });
        }
    }

    // Proxy for recent violations if needed, reusing the logic from ViolationsController or just calling it directly if convenient,
    // but typically Dashboard might have a specific endpoint. 
    // The main DashboardController has GetRecentViolations but it requires explicit header passing from client.
    // Here we auto-inject it.
    [HttpGet("violations/recent")]
    public async Task<IActionResult> GetRecentViolations()
    {
         try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            
            // Reusing the same endpoint as generic get violations but maybe limiting count?
            // The downstream /api/violations gets all. 
            // The Main DashboardController.cs had a specific logic for "recent".
            // Let's call the generic /api/violations and let frontend filter/limit or add limit param to downstream.
            
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/violations");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error fetching recent violations for dashboard");
            return StatusCode(500, new { error = "Failed to fetch recent violations" });
        }
    }
}
