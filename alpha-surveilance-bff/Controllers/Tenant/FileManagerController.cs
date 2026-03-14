using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace alpha_surveilance_bff.Controllers.Tenant;

/// <summary>
/// BFF proxy for the File Manager endpoints in the Violation Management API.
/// All routes forward the JWT via header propagation; the downstream API performs
/// tenant isolation using the tenantId claim embedded in the bearer token.
/// </summary>
[ApiController]
[Route("api/filemanager")]
[Authorize(Roles = "TenantAdmin")]
public class FileManagerController(IHttpClientFactory httpClientFactory, ILogger<FileManagerController> logger) : ControllerBase
{
    // ─── FOLDERS ─────────────────────────────────────────────────────────────

    [HttpGet("folders")]
    public Task<IActionResult> GetRootFolders() =>
        Forward(client => client.GetAsync("/api/filemanager/folders"));

    [HttpGet("folders/{id:guid}")]
    public Task<IActionResult> GetFolderContents(Guid id) =>
        Forward(client => client.GetAsync($"/api/filemanager/folders/{id}"));

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] JsonElement body) =>
        await Forward(client => client.PostAsync("/api/filemanager/folders",
            new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")));

    [HttpPatch("folders/{id:guid}")]
    public async Task<IActionResult> RenameFolder(Guid id, [FromBody] JsonElement body) =>
        await Forward(client => client.PatchAsync($"/api/filemanager/folders/{id}",
            new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")));

    [HttpDelete("folders/{id:guid}")]
    public Task<IActionResult> DeleteFolder(Guid id) =>
        Forward(client => client.DeleteAsync($"/api/filemanager/folders/{id}"));

    // ─── FILES ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload: parses the incoming multipart form, then rebuilds and forwards it downstream.
    /// Raw body-streaming is avoided because ASP.NET Core already consumes the body for model binding,
    /// which would silently drop parentFolderId if we tried to re-stream Request.Body.
    /// </summary>
    [HttpPost("files/upload")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> UploadFiles([FromForm] List<IFormFile> files, [FromForm] string? parentFolderId)
    {
        try
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { error = "No files provided." });

            var client = httpClientFactory.CreateClient("ViolationApi");

            // Rebuild the multipart form so parentFolderId is reliably forwarded
            using var form = new MultipartFormDataContent();

            foreach (var file in files)
            {
                var fileStream = file.OpenReadStream();
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
                form.Add(fileContent, "files", file.FileName);
            }

            if (!string.IsNullOrEmpty(parentFolderId))
                form.Add(new StringContent(parentFolderId), "parentFolderId");

            var response = await client.PostAsync("/api/filemanager/files/upload", form);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading files to file manager");
            return StatusCode(500, new { error = "Failed to upload files" });
        }
    }

    [HttpPatch("files/{id:guid}")]
    public async Task<IActionResult> RenameFile(Guid id, [FromBody] JsonElement body) =>
        await Forward(client => client.PatchAsync($"/api/filemanager/files/{id}",
            new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")));

    [HttpDelete("files/{id:guid}")]
    public Task<IActionResult> DeleteFile(Guid id) =>
        Forward(client => client.DeleteAsync($"/api/filemanager/files/{id}"));

    // ─── SEARCH ──────────────────────────────────────────────────────────────

    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery] string q) =>
        Forward(client => client.GetAsync($"/api/filemanager/search?q={Uri.EscapeDataString(q ?? "")}"));

    // ─── HELPER ──────────────────────────────────────────────────────────────

    private async Task<IActionResult> Forward(Func<HttpClient, Task<HttpResponseMessage>> call)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ViolationApi");
            var response = await call(client);
            return await ProxyResponse(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File manager proxy error");
            return StatusCode(500, new { error = "Request failed" });
        }
    }

    private static async Task<IActionResult> ProxyResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return new NoContentResult();

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
            return new StatusCodeResult((int)response.StatusCode);

        return new ObjectResult(JsonSerializer.Deserialize<JsonElement>(json))
            { StatusCode = (int)response.StatusCode };
    }
}
