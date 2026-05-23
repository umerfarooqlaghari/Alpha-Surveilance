using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class ViolationsController : ProxyControllerBase
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
            return await ProxyResponse(response);
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
            return await ProxyResponse(response);
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching violation {ViolationId}", id);
            return StatusCode(500, new { error = "Failed to fetch violation" });
        }
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics([FromQuery] string? startDate, [FromQuery] string? endDate, [FromQuery] string? cameraId, [FromQuery] Guid? locationId)
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
            if (locationId.HasValue && locationId.Value != Guid.Empty) query["locationId"] = locationId.Value.ToString();

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

    // ─────────────────────────────────────────────────────────────────────
    // False-positive proxy endpoints. The downstream API holds the auth
    // tenant-scoping logic — we just forward with the X-Tenant-Id header
    // and the caller's identity (via the JWT bearer attached upstream).
    // ─────────────────────────────────────────────────────────────────────

    [HttpGet("false-positives")]
    public async Task<IActionResult> GetFalsePositives()
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/violations/false-positives");
            request.Headers.Add("X-Tenant-Id", tenantId);
            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching false-positive violations");
            return StatusCode(500, new { error = "Failed to fetch false-positive violations" });
        }
    }

    public class FalsePositiveMarkBody { public List<Guid> ViolationIds { get; set; } = new(); public string? Reason { get; set; } }
    public class FalsePositiveUnmarkBody { public List<Guid> ViolationIds { get; set; } = new(); }

    [HttpPost("false-positives/mark")]
    public async Task<IActionResult> MarkFalsePositive([FromBody] FalsePositiveMarkBody body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");
            if (body?.ViolationIds == null || body.ViolationIds.Count == 0)
                return BadRequest(new { error = "ViolationIds is required" });

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/violations/false-positives/mark")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tenant-Id", tenantId);
            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking false-positive violations");
            return StatusCode(500, new { error = "Failed to mark violations as false-positive" });
        }
    }

    [HttpPost("false-positives/unmark")]
    public async Task<IActionResult> UnmarkFalsePositive([FromBody] FalsePositiveUnmarkBody body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");
            if (body?.ViolationIds == null || body.ViolationIds.Count == 0)
                return BadRequest(new { error = "ViolationIds is required" });

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/violations/false-positives/unmark")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tenant-Id", tenantId);
            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmarking false-positive violations");
            return StatusCode(500, new { error = "Failed to restore violations" });
        }
    }
}
