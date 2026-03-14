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

    public SopsController(ISopService sopService, ILogger<SopsController> logger)
    {
        _sopService = sopService;
        _logger = logger;
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
}
