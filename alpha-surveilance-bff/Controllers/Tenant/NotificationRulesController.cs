using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class NotificationRulesController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationRulesController> _logger;

    public NotificationRulesController(IHttpClientFactory httpClientFactory, ILogger<NotificationRulesController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        var tenantId = User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

        var client = _httpClientFactory.CreateClient("ViolationApi");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/notificationrules");
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await client.SendAsync(request);
        return await ProxyResponse(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRule()
    {
        var tenantId = User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

        var client = _httpClientFactory.CreateClient("ViolationApi");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notificationrules")
        {
            Content = new StreamContent(Request.Body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await client.SendAsync(request);
        return await ProxyResponse(response);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRule(Guid id)
    {
        var tenantId = User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

        var client = _httpClientFactory.CreateClient("ViolationApi");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/notificationrules/{id}")
        {
            Content = new StreamContent(Request.Body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await client.SendAsync(request);
        return await ProxyResponse(response);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        var tenantId = User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

        var client = _httpClientFactory.CreateClient("ViolationApi");
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/notificationrules/{id}");
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await client.SendAsync(request);
        return await ProxyResponse(response);
    }
}
