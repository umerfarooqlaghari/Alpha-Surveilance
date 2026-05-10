using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
public class LocationsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(IHttpClientFactory httpClientFactory, ILogger<LocationsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateLocation([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/locations", content);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location");
            return StatusCode(500, new { error = "Failed to create location" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocations([FromQuery] Guid tenantId, [FromQuery] string? search)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["tenantId"] = tenantId.ToString();
            if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;

            var response = await client.GetAsync($"/api/locations?{query}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching locations");
            return StatusCode(500, new { error = "Failed to fetch locations" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLocation(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/locations/{id}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching location {LocationId}", id);
            return StatusCode(500, new { error = "Failed to fetch location" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"/api/locations/{id}", content);
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
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/locations/{id}");

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
