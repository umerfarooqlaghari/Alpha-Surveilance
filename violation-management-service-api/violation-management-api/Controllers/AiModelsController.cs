using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/ai-models")]
public class AiModelsController(
    IAiModelService aiModelService,
    ILogger<AiModelsController> logger) : ControllerBase
{
    // ════════════════════════════════════════════════════════════════════════
    // SUPERADMIN — model registry management
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>List all registered AI models with their current status and SOP rule count.</summary>
    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAll()
    {
        var models = await aiModelService.GetAllAsync();
        return Ok(models);
    }

    /// <summary>Get a single AI model by id.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var model = await aiModelService.GetByIdAsync(id);
        if (model is null) return NotFound();
        return Ok(model);
    }

    /// <summary>Register a new AI model in the registry.</summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Register([FromBody] RegisterAiModelRequest request)
    {
        try
        {
            var created = await aiModelService.RegisterAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error registering AI model");
            return StatusCode(500, new { error = "Failed to register model" });
        }
    }

    /// <summary>Update AI model metadata and download configuration.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RegisterAiModelRequest request)
    {
        try
        {
            var updated = await aiModelService.UpdateAsync(id, request);
            if (updated is null) return NotFound();
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating AI model {ModelId}", id);
            return StatusCode(500, new { error = "Failed to update model" });
        }
    }

    /// <summary>Enable this model — sets status to Available so the inference service will load it.</summary>
    [HttpPost("{id:guid}/enable")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var ok = await aiModelService.EnableAsync(id);
        if (!ok) return NotFound();
        return NoContent();
    }

    /// <summary>Disable this model — inference service will skip all rules that reference it.</summary>
    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Disable(Guid id)
    {
        var ok = await aiModelService.DisableAsync(id);
        if (!ok) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Soft-delete. Rejected if any SOP violation types still reference this model.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var (success, error) = await aiModelService.DeleteAsync(id);
        if (!success && error is not null)
            return Conflict(new { error });
        if (!success)
            return NotFound();
        return NoContent();
    }

    // ════════════════════════════════════════════════════════════════════════
    // EDGE DEVICE — reports download outcome back to the registry
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the vision inference service after completing (or failing)
    /// a model download. Updates Status, Checksum, FileSize and DownloadedAt.
    /// Protected by InternalApiKeyMiddleware — no JWT required.
    /// </summary>
    [HttpPatch("internal/{id:guid}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateEdgeStatus(Guid id, [FromBody] EdgeModelStatusUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.Status))
            return BadRequest(new { error = "Status is required." });
        try
        {
            var ok = await aiModelService.UpdateEdgeStatusAsync(id, update);
            if (!ok) return NotFound();
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
