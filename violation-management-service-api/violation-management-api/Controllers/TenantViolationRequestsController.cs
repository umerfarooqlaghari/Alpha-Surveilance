using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;
using AlphaSurveilance.Services.Interfaces; // For ICurrentTenantService

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantViolationRequestsController : ControllerBase
{
    private readonly ITenantViolationRequestService _requestService;
    private readonly ICurrentTenantService _currentTenantService;

    public TenantViolationRequestsController(
        ITenantViolationRequestService requestService,
        ICurrentTenantService currentTenantService)
    {
        _requestService = requestService;
        _currentTenantService = currentTenantService;
    }

    // --- Tenant Admin Endpoints ---

    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateTenantViolationRequestDto requestDto)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue) return Forbid();

        try
        {
            var result = await _requestService.CreateRequestAsync(tenantId.Value, requestDto);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("my-requests")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> GetMyRequests()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue) return Forbid();

        var requests = await _requestService.GetRequestsByTenantAsync(tenantId.Value);
        return Ok(requests);
    }

    [HttpGet("approved/{tenantId}")]
    [Authorize(Policy = "SuperOrTenantAdmin")]
    public async Task<IActionResult> GetApprovedRequests(Guid tenantId)
    {
        // If TenantAdmin, ensure they only fetch their own
        if (!_currentTenantService.IsSuperAdmin)
        {
            var myTenantId = _currentTenantService.TenantId;
            if (myTenantId != tenantId) return Forbid();
        }

        var requests = await _requestService.GetApprovedRequestsByTenantAsync(tenantId);
        return Ok(requests);
    }

    // --- Super Admin Endpoints ---

    [HttpGet("pending")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var requests = await _requestService.GetAllPendingRequestsAsync();
        return Ok(requests);
    }

    [HttpPatch("{id}/resolve")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> ResolveRequest(Guid id, [FromBody] ResolveTenantViolationRequestDto requestDto)
    {
        try
        {
            var result = await _requestService.ResolveRequestAsync(id, requestDto.Status);
            if (result == null) return NotFound(new { error = "Request not found" });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("assign-proactive")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> AssignProactive([FromBody] ProactiveAssignDto dto)
    {
        try
        {
            var result = await _requestService.AssignProactiveRequestAsync(dto.TenantId, dto.SopViolationTypeId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("all")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAllRequests()
    {
        var requests = await _requestService.GetAllRequestsAsync();
        return Ok(requests);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Unassign(Guid id)
    {
        var result = await _requestService.UnassignAsync(id);
        if (!result) return NotFound(new { error = "Association not found" });
        return NoContent();
    }
}

public class ProactiveAssignDto
{
    public Guid TenantId { get; set; }
    public Guid SopViolationTypeId { get; set; }
}
