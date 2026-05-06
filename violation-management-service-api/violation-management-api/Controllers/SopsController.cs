using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Require basic authentication for all
public class SopsController : ControllerBase
{
    private readonly ISopService _sopService;
    private readonly ILogger<SopsController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SopsController(
        ISopService sopService,
        ILogger<SopsController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _sopService = sopService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> CreateSop([FromBody] CreateSopRequest request)
    {
        var sop = await _sopService.CreateSopAsync(request);
        return CreatedAtAction(nameof(GetSop), new { id = sop.Id }, sop);
    }

    [HttpGet]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> GetAllSops()
    {
        var sops = await _sopService.GetAllSopsAsync();
        return Ok(sops);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> GetSop(Guid id)
    {
        var sop = await _sopService.GetSopByIdAsync(id);
        if (sop == null) return NotFound(new { error = "SOP not found" });
        return Ok(sop);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateSop(Guid id, [FromBody] UpdateSopRequest request)
    {
        var sop = await _sopService.UpdateSopAsync(id, request);
        if (sop == null) return NotFound(new { error = "SOP not found" });
        return Ok(sop);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DeleteSop(Guid id)
    {
        var result = await _sopService.DeleteSopAsync(id);
        if (!result) return NotFound(new { error = "SOP not found" });
        return NoContent();
    }

    // --- VIOLATION TYPES MANAGEMENT ---

    [HttpPost("{sopId}/violations")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> AddViolationType(Guid sopId, [FromBody] CreateSopViolationTypeRequest request)
    {
        try
        {
            var violationType = await _sopService.CreateSopViolationTypeAsync(sopId, request);
            return Ok(violationType);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("violations/{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateViolationType(Guid id, [FromBody] UpdateSopViolationTypeRequest request)
    {
        var violationType = await _sopService.UpdateSopViolationTypeAsync(id, request);
        if (violationType == null) return NotFound(new { error = "Violation type not found" });
        return Ok(violationType);
    }

    [HttpDelete("violations/{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DeleteViolationType(Guid id)
    {
        var result = await _sopService.DeleteSopViolationTypeAsync(id);
        if (!result) return NotFound(new { error = "Violation type not found" });
        return NoContent();
    }

    /// <summary>
    /// Add/remove/replace the detection labels for a violation type without
    /// touching the rest of the record. Hot-reloads the Vision Service so the
    /// new label set takes effect within the next poll cycle.
    /// </summary>
    [HttpPatch("violations/{id}/labels")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateTriggerLabels(Guid id, [FromBody] UpdateTriggerLabelsRequest request)
    {
        if (request?.Labels == null)
            return BadRequest(new { error = "labels is required" });

        var updated = await _sopService.UpdateTriggerLabelsAsync(id, request.Labels);
        if (updated == null) return NotFound(new { error = "Violation type not found" });
        return Ok(updated);
    }

    /// <summary>
    /// Run inference against arbitrary candidate labels on an uploaded image
    /// without persisting anything. Used by the SOP UI to validate a new label
    /// (e.g. "apron") before saving it.
    /// </summary>
    [HttpPost("violations/preview")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    [RequestSizeLimit(20_000_000)] // 20 MB
    public async Task<IActionResult> PreviewDetection(
        IFormFile file,
        [FromForm] string labels,
        [FromForm] string? modelIdentifier = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "file is required" });
        if (string.IsNullOrWhiteSpace(labels))
            return BadRequest(new { error = "labels is required" });

        var visionBaseUrl = _configuration["VisionService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(visionBaseUrl))
            return StatusCode(503, new { error = "VisionService:BaseUrl is not configured." });

        try
        {
            using var form = new MultipartFormDataContent();
            using var fileStream = file.OpenReadStream();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
            form.Add(fileContent, "file", file.FileName);
            form.Add(new StringContent(labels), "trigger_labels");
            form.Add(new StringContent(modelIdentifier ?? "hygiene-monitor"), "model_identifier");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            var resp = await client.PostAsync($"{visionBaseUrl.TrimEnd('/')}/preview-detection", form);
            var body = await resp.Content.ReadAsStringAsync();
            return new ContentResult
            {
                StatusCode = (int)resp.StatusCode,
                Content = body,
                ContentType = "application/json",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vision Service preview-detection call failed");
            return StatusCode(502, new { error = $"Vision Service unavailable: {ex.Message}" });
        }
    }
}
