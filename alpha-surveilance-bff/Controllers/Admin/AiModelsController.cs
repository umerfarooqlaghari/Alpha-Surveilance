using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Admin;

[ApiController]
[Route("api/ai-models")]
[Route("api/admin/ai-models")]
public class AiModelsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiModelsController> _logger;

    public AiModelsController(IHttpClientFactory httpClientFactory, ILogger<AiModelsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/ai-models");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AI models");
            return StatusCode(500, new { error = "Failed to fetch AI models" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/ai-models/{id}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AI model {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch AI model" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/ai-models", content);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering AI model");
            return StatusCode(500, new { error = "Failed to register AI model" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"/api/ai-models/{id}", content);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating AI model {Id}", id);
            return StatusCode(500, new { error = "Failed to update AI model" });
        }
    }

    [HttpPost("{id}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.PostAsync($"/api/ai-models/{id}/enable", null);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling AI model {Id}", id);
            return StatusCode(500, new { error = "Failed to enable AI model" });
        }
    }

    [HttpPost("{id}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.PostAsync($"/api/ai-models/{id}/disable", null);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling AI model {Id}", id);
            return StatusCode(500, new { error = "Failed to disable AI model" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/ai-models/{id}");
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AI model {Id}", id);
            return StatusCode(500, new { error = "Failed to delete AI model" });
        }
    }
}
