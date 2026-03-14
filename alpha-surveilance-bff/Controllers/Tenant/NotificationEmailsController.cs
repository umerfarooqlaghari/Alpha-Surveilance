using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/notification-emails")]
[Authorize(Roles = "TenantAdmin")]
public class NotificationEmailsController(IHttpClientFactory httpClientFactory, ILogger<NotificationEmailsController> logger) : ControllerBase
{
    private readonly IHttpClientFactory _factory = httpClientFactory;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var client = _factory.CreateClient("ViolationApi");
            var response = await client.GetAsync("/api/notification-emails");
            return await Proxy(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching notification emails");
            return StatusCode(500, new { error = "Failed to fetch notification emails" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] JsonElement body)
    {
        try
        {
            var client = _factory.CreateClient("ViolationApi");
            var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/notification-emails", content);
            return await Proxy(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding notification email");
            return StatusCode(500, new { error = "Failed to add notification email" });
        }
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id)
    {
        try
        {
            var client = _factory.CreateClient("ViolationApi");
            var response = await client.PatchAsync($"/api/notification-emails/{id}/toggle", null);
            return await Proxy(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling notification email {Id}", id);
            return StatusCode(500, new { error = "Failed to toggle notification email" });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var client = _factory.CreateClient("ViolationApi");
            var response = await client.DeleteAsync($"/api/notification-emails/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return NoContent();
            return await Proxy(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting notification email {Id}", id);
            return StatusCode(500, new { error = "Failed to delete notification email" });
        }
    }

    private static async Task<IActionResult> Proxy(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json)) return new StatusCodeResult((int)response.StatusCode);
        return new ObjectResult(JsonSerializer.Deserialize<JsonElement>(json))
            { StatusCode = (int)response.StatusCode };
    }
}
