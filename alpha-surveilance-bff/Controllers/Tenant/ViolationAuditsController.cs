using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class ViolationAuditsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ViolationAuditsController> _logger;

    public ViolationAuditsController(IHttpClientFactory httpClientFactory, ILogger<ViolationAuditsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? tenantId)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(tenantId))
            request.Headers.Add("X-Tenant-Id", tenantId);
        return request;
    }

    /// <summary>Safely deserializes a JSON string. Falls back to a plain error object if content is not valid JSON.</summary>
    private IActionResult SafeJson(System.Net.HttpStatusCode status, string content)
    {
        try { return StatusCode((int)status, JsonSerializer.Deserialize<JsonElement>(content)); }
        catch { return StatusCode((int)status, new { error = content }); }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var req = BuildRequest(HttpMethod.Get, "/api/violationaudits", tenantId);
            var response = await client.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            return SafeJson(response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching violation audits");
            return StatusCode(500, new { error = "Failed to fetch audits" });
        }
    }

    [HttpGet("violation/{violationId}")]
    public async Task<IActionResult> GetByViolation(Guid violationId)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var req = BuildRequest(HttpMethod.Get, $"/api/violationaudits/violation/{violationId}", tenantId);
            var response = await client.SendAsync(req);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return NotFound();

            var content = await response.Content.ReadAsStringAsync();
            return SafeJson(response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching audit for violation {ViolationId}", violationId);
            return StatusCode(500, new { error = "Failed to fetch audit" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var req = BuildRequest(HttpMethod.Post, "/api/violationaudits", tenantId);
            req.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            return SafeJson(response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating violation audit");
            return StatusCode(500, new { error = "Failed to create audit" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var req = BuildRequest(HttpMethod.Put, $"/api/violationaudits/{id}", tenantId);
            req.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            return SafeJson(response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating violation audit {Id}", id);
            return StatusCode(500, new { error = "Failed to update audit" });
        }
    }
}
