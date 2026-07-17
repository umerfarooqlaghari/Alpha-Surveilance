using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class DashboardController : ProxyControllerBase
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
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard stats for tenant");
            return StatusCode(500, new { error = "Failed to fetch dashboard stats" });
        }
    }

    /// <summary>
    /// Recent violations for the live-feed dashboard. Bounded by design: the
    /// downstream /api/violations endpoint supports ?limit= (newest first), so
    /// this proxy defaults to 50 rows and never requests more than 200 — the
    /// dashboard only renders a short activity feed and must not pull the
    /// tenant's entire violation history on every page load.
    /// </summary>
    [HttpGet("violations/recent")]
    public async Task<IActionResult> GetRecentViolations([FromQuery] int? limit = null)
    {
         try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");

            var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/violations?limit={effectiveLimit}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error fetching recent violations for dashboard");
            return StatusCode(500, new { error = "Failed to fetch recent violations" });
        }
    }
}
