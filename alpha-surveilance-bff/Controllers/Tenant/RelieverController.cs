using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class RelieverController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RelieverController> _logger;

    public RelieverController(IHttpClientFactory httpClientFactory, ILogger<RelieverController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Workstations ────────────────────────────────────────────────────────
    [HttpGet("workstations")]
    public async Task<IActionResult> GetWorkstations([FromQuery] Guid? locationId, [FromQuery] Guid? cameraId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (locationId.HasValue) query["locationId"] = locationId.Value.ToString();
            if (cameraId.HasValue) query["cameraId"] = cameraId.Value.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workstations?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying GetWorkstations");
            return StatusCode(500, new { error = "Failed to fetch workstations" });
        }
    }

    [HttpGet("workstations/{id:guid}")]
    public async Task<IActionResult> GetWorkstationById(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workstations/{id}?tenantId={tenantId}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying GetWorkstationById {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch workstation details" });
        }
    }

    [HttpPost("workstations")]
    public async Task<IActionResult> CreateWorkstation([FromBody] JsonElement payload)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workstations?tenantId={tenantId}")
            {
                Content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying CreateWorkstation");
            return StatusCode(500, new { error = "Failed to create workstation" });
        }
    }

    [HttpPut("workstations/{id:guid}")]
    public async Task<IActionResult> UpdateWorkstation(Guid id, [FromBody] JsonElement payload)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/workstations/{id}?tenantId={tenantId}")
            {
                Content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying UpdateWorkstation {Id}", id);
            return StatusCode(500, new { error = "Failed to update workstation" });
        }
    }

    [HttpDelete("workstations/{id:guid}")]
    public async Task<IActionResult> DeleteWorkstation(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workstations/{id}?tenantId={tenantId}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying DeleteWorkstation {Id}", id);
            return StatusCode(500, new { error = "Failed to delete workstation" });
        }
    }

    [HttpPost("workstations/{id:guid}/assignments")]
    public async Task<IActionResult> AssignWorkers(Guid id, [FromBody] JsonElement payload)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workstations/{id}/assignments?tenantId={tenantId}")
            {
                Content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying AssignWorkers {Id}", id);
            return StatusCode(500, new { error = "Failed to update worker assignments" });
        }
    }

    // ── Live Board & Sessions ───────────────────────────────────────────────
    [HttpGet("live-board")]
    public async Task<IActionResult> GetLiveBoard([FromQuery] Guid? locationId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (locationId.HasValue) query["locationId"] = locationId.Value.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/relieftracking/live-board?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying GetLiveBoard");
            return StatusCode(500, new { error = "Failed to fetch live board" });
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? workstationId,
        [FromQuery] string? status,
        [FromQuery] string? employeeExternalId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (startDate.HasValue) query["startDate"] = startDate.Value.ToString("yyyy-MM-dd");
            if (endDate.HasValue) query["endDate"] = endDate.Value.ToString("yyyy-MM-dd");
            if (workstationId.HasValue) query["workstationId"] = workstationId.Value.ToString();
            if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
            if (!string.IsNullOrWhiteSpace(employeeExternalId)) query["employeeExternalId"] = employeeExternalId;

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/relieftracking/sessions?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying GetSessions");
            return StatusCode(500, new { error = "Failed to fetch relief sessions" });
        }
    }

    // ── Analytics ───────────────────────────────────────────────────────────
    [HttpGet("analytics/summary")]
    public async Task<IActionResult> GetAnalyticsSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? locationId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (startDate.HasValue) query["startDate"] = startDate.Value.ToString("yyyy-MM-dd");
            if (endDate.HasValue) query["endDate"] = endDate.Value.ToString("yyyy-MM-dd");
            if (locationId.HasValue) query["locationId"] = locationId.Value.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/reliefanalytics/summary?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying GetAnalyticsSummary");
            return StatusCode(500, new { error = "Failed to fetch relief analytics" });
        }
    }
}
