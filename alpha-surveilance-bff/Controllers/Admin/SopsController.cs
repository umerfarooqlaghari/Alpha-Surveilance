using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
public class SopsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SopsController> _logger;

    public SopsController(IHttpClientFactory httpClientFactory, ILogger<SopsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateSop([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/sops", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SOP");
            return StatusCode(500, new { error = "Failed to create SOP" });
        }
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
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching SOPs");
            return StatusCode(500, new { error = "Failed to fetch SOPs" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetSop(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/sops/{id}");

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching SOP {SopId}", id);
            return StatusCode(500, new { error = "Failed to fetch SOP" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSop(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"/api/sops/{id}", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SOP {SopId}", id);
            return StatusCode(500, new { error = "Failed to update SOP" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSop(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/sops/{id}");

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return NoContent();

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting SOP {SopId}", id);
            return StatusCode(500, new { error = "Failed to delete SOP" });
        }
    }

    // --- VIOLATION TYPES MANAGEMENT ---

    [HttpPost("{sopId}/violations")]
    public async Task<IActionResult> AddViolationType(Guid sopId, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"/api/sops/{sopId}/violations", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding violation type to SOP {SopId}", sopId);
            return StatusCode(500, new { error = "Failed to add violation type" });
        }
    }

    [HttpPut("violations/{id}")]
    public async Task<IActionResult> UpdateViolationType(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"/api/sops/violations/{id}", content);

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating violation type {ViolationTypeId}", id);
            return StatusCode(500, new { error = "Failed to update violation type" });
        }
    }

    [HttpDelete("violations/{id}")]
    public async Task<IActionResult> DeleteViolationType(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/sops/violations/{id}");

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return NoContent();

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent)) return StatusCode((int)response.StatusCode);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting violation type {ViolationTypeId}", id);
            return StatusCode(500, new { error = "Failed to delete violation type" });
        }
    }
}
