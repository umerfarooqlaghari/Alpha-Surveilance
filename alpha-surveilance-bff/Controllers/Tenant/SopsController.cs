using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
public class SopsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SopsController> _logger;

    public SopsController(IHttpClientFactory httpClientFactory, ILogger<SopsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllSops()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/sops");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching SOPs for tenant");
            return StatusCode(500, new { error = "Failed to fetch SOPs" });
        }
    }

    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyRequests()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/tenantviolationrequests/my-requests");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tenant requests");
            return StatusCode(500, new { error = "Failed to fetch requests" });
        }
    }

    [HttpPost("request")]
    public async Task<IActionResult> CreateRequest([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/tenantviolationrequests", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(responseContent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant request");
            return StatusCode(500, new { error = "Failed to create request" });
        }
    }
}
