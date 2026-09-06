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
    public class ReliefTrackingController : ControllerBase
    {
        private readonly IReliefTrackingService _reliefTrackingService;
        private readonly ICurrentTenantService _currentTenantService;
        private readonly ILogger<ReliefTrackingController> _logger;

        public ReliefTrackingController(
            IReliefTrackingService reliefTrackingService,
            ICurrentTenantService currentTenantService,
            ILogger<ReliefTrackingController> logger)
        {
            _reliefTrackingService = reliefTrackingService;
            _currentTenantService = currentTenantService;
            _logger = logger;
        }

        /// <summary>
        /// Internal webhook endpoint called by Vision Inference Service when a worker leaves or enters a workstation polygon.
        /// Auth handled by InternalApiKeyMiddleware.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("internal/events")]
        public async Task<IActionResult> RecordReliefEvent([FromBody] ReliefEventIngestRequest request)
        {
            if (request == null || request.WorkstationId == Guid.Empty)
            {
                return BadRequest("Invalid relief event payload.");
            }

            try
            {
                var result = await _reliefTrackingService.ProcessReliefEventAsync(request);
                return Ok(result ?? new ReliefSessionResponse());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing internal relief event for workstation {WorkstationId}", request.WorkstationId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("sessions")]
        public async Task<ActionResult<List<ReliefSessionResponse>>> GetSessions(
            [FromQuery] Guid? tenantId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] Guid? workstationId,
            [FromQuery] string? status,
            [FromQuery] string? employeeExternalId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            var result = await _reliefTrackingService.GetSessionsAsync(
                effectiveTenantId, startDate, endDate, workstationId, status, employeeExternalId);
            return Ok(result);
        }

        [HttpGet("live-board")]
        public async Task<ActionResult<List<WorkstationLiveBoardItemResponse>>> GetLiveBoard(
            [FromQuery] Guid? tenantId,
            [FromQuery] Guid? locationId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            var result = await _reliefTrackingService.GetLiveBoardAsync(effectiveTenantId, locationId);
            return Ok(result);
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
