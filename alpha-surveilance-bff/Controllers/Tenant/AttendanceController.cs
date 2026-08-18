using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class AttendanceController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(IHttpClientFactory httpClientFactory, ILogger<AttendanceController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAttendanceRecords(
        [FromQuery] DateTime? shiftDate,
        [FromQuery] string? employeeExternalId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");

            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (shiftDate.HasValue) query["shiftDate"] = shiftDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrWhiteSpace(employeeExternalId)) query["employeeExternalId"] = employeeExternalId;
            if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
            query["page"] = page.ToString();
            query["pageSize"] = pageSize.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/attendance?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching attendance records for tenant");
            return StatusCode(500, new { error = "Failed to fetch attendance records" });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetAttendanceSummary([FromQuery] DateTime? shiftDate)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");

            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId;
            if (shiftDate.HasValue) query["shiftDate"] = shiftDate.Value.ToString("yyyy-MM-dd");

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/attendance/summary?{query}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching attendance summary for tenant");
            return StatusCode(500, new { error = "Failed to fetch attendance summary" });
        }
    }
}
