using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
public class TenantViolationRequestsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TenantViolationRequestsController> _logger;

    public TenantViolationRequestsController(IHttpClientFactory httpClientFactory, ILogger<TenantViolationRequestsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // --- Super Admin Endpoints ---

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/tenantviolationrequests/pending");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending requests");
            return StatusCode(500, new { error = "Failed to fetch pending requests" });
        }
    }

    [HttpGet("approved/{tenantId}")]
    public async Task<IActionResult> GetApprovedRequests(Guid tenantId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/tenantviolationrequests/approved/{tenantId}");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching approved requests for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to fetch approved requests" });
        }
    }

    [HttpPatch("{id}/resolve")]
    public async Task<IActionResult> ResolveRequest(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/tenantviolationrequests/{id}/resolve")
            {
                Content = content
            };
            var response = await client.SendAsync(httpRequest);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving request {RequestId}", id);
            return StatusCode(500, new { error = "Failed to resolve request" });
        }
    }

    [HttpPost("assign-proactive")]
    public async Task<IActionResult> AssignProactive([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/tenantviolationrequests/assign-proactive", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proactively assigning request");
            return StatusCode(500, new { error = "Failed to aggressively assign request" });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllRequests()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/tenantviolationrequests/all");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all requests");
            return StatusCode(500, new { error = "Failed to fetch all requests" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Unassign(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/tenantviolationrequests/{id}");

            if (response.IsSuccessStatusCode) return NoContent();

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unassigning request {RequestId}", id);
            return StatusCode(500, new { error = "Failed to unassign request" });
        }
    }
}
