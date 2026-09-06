using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AlphaSurveilance.Services.Interfaces;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkstationsController : ControllerBase
    {
        private readonly IWorkstationService _workstationService;
        private readonly ICurrentTenantService _currentTenantService;
        private readonly ILogger<WorkstationsController> _logger;

        public WorkstationsController(
            IWorkstationService workstationService,
            ICurrentTenantService currentTenantService,
            ILogger<WorkstationsController> logger)
        {
            _workstationService = workstationService;
            _currentTenantService = currentTenantService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<WorkstationResponse>>> GetWorkstations(
            [FromQuery] Guid? tenantId,
            [FromQuery] Guid? locationId,
            [FromQuery] Guid? cameraId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            var result = await _workstationService.GetWorkstationsAsync(effectiveTenantId, locationId, cameraId);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<WorkstationDetailResponse>> GetWorkstationById(Guid id, [FromQuery] Guid? tenantId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            var result = await _workstationService.GetWorkstationByIdAsync(effectiveTenantId, id);
            if (result == null) return NotFound("Workstation not found.");
            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<WorkstationResponse>> CreateWorkstation(
            [FromBody] CreateWorkstationRequest request,
            [FromQuery] Guid? tenantId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            try
            {
                var result = await _workstationService.CreateWorkstationAsync(effectiveTenantId, request);
                return CreatedAtAction(nameof(GetWorkstationById), new { id = result.Id, tenantId = effectiveTenantId }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating workstation");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<WorkstationResponse>> UpdateWorkstation(
            Guid id,
            [FromBody] UpdateWorkstationRequest request,
            [FromQuery] Guid? tenantId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            try
            {
                var result = await _workstationService.UpdateWorkstationAsync(effectiveTenantId, id, request);
                if (result == null) return NotFound("Workstation not found.");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating workstation {Id}", id);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteWorkstation(Guid id, [FromQuery] Guid? tenantId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            var deleted = await _workstationService.DeleteWorkstationAsync(effectiveTenantId, id);
            if (!deleted) return NotFound("Workstation not found.");
            return NoContent();
        }

        [HttpPost("{id:guid}/assignments")]
        public async Task<ActionResult<List<WorkstationAssignmentResponse>>> AssignWorkers(
            Guid id,
            [FromBody] AssignWorkstationWorkersRequest request,
            [FromQuery] Guid? tenantId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            try
            {
                var result = await _workstationService.AssignWorkersAsync(effectiveTenantId, id, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning workers to workstation {Id}", id);
                return BadRequest(new { error = ex.Message });
            }
        }

        private Guid ResolveTenantId(Guid? tenantId)
        {
            if (_currentTenantService.IsSuperAdmin)
            {
                return tenantId ?? _currentTenantService.TenantId ?? Guid.Empty;
            }
            return _currentTenantService.TenantId ?? tenantId ?? Guid.Empty;
        }
    }
}
