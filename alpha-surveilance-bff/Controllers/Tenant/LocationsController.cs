using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
[Authorize(Roles = "TenantAdmin")]
public class LocationsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(IHttpClientFactory httpClientFactory, ILogger<LocationsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetLocations([FromQuery] string? search)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;

            var url = query.Count > 0 ? $"/api/locations?{query}" : "/api/locations";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching locations for tenant");
            return StatusCode(500, new { error = "Failed to fetch locations" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLocation(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/locations/{id}");
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(request);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching location {LocationId}", id);
            return StatusCode(500, new { error = "Failed to fetch location" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateLocation([FromBody] JsonElement body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            // Inject tenantId into the body — JWT claim wins over any client-supplied value
            var requestObj = JsonSerializer.Deserialize<Dictionary<string, object>>(body.GetRawText())
                             ?? new Dictionary<string, object>();
            requestObj["tenantId"] = tenantId;

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/locations")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location");
            return StatusCode(500, new { error = "Failed to create location" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] JsonElement body)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var httpRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/locations/{id}")
            {
                Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location {LocationId}", id);
            return StatusCode(500, new { error = "Failed to update location" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteLocation(Guid id)
    {
        try
        {
            var tenantId = User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant ID not found in token");

            var client = _httpClientFactory.CreateClient("ViolationApi");
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/locations/{id}");
            httpRequest.Headers.Add("X-Tenant-Id", tenantId);

            var response = await client.SendAsync(httpRequest);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return NoContent();

            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location {LocationId}", id);
            return StatusCode(500, new { error = "Failed to delete location" });
        }
    }
}
