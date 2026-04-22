using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers;

/// <summary>
/// Shared base for all BFF proxy controllers.
/// Provides a safe ProxyResponse helper that handles empty bodies
/// (e.g., 401/204/NoContent) without crashing on JSON deserialization.
/// </summary>
public abstract class ProxyControllerBase : ControllerBase
{
    protected static async Task<IActionResult> ProxyResponse(HttpResponseMessage response)
    {
        var responseContent = await response.Content.ReadAsStringAsync();

        // No body — return status only (204 NoContent, or empty 401 etc.)
        if (string.IsNullOrWhiteSpace(responseContent))
            return new StatusCodeResult((int)response.StatusCode);

        // Try to parse as JSON and proxy it through as-is
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return new ObjectResult(json) { StatusCode = (int)response.StatusCode };
        }
        catch
        {
            // If it's not valid JSON (e.g. plain-text error body), wrap it
            return new ObjectResult(new { error = responseContent }) { StatusCode = (int)response.StatusCode };
        }
    }
}
