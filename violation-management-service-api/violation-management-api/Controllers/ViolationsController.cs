using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlphaSurveilance.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ViolationsController(
        IViolationService violationService,
        ICurrentTenantService currentTenantService) : ControllerBase
    {
        private string GetTenantId()
        {
            var tenantId = currentTenantService.TenantId;
            if (!tenantId.HasValue)
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            return tenantId.Value.ToString();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ViolationResponse>> GetViolation(Guid id)
        {
            var tenantId = GetTenantId();
            var violation = await violationService.GetViolationAsync(id, tenantId);
            if (violation == null) return NotFound();
            return Ok(violation);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ViolationResponse>>> GetViolations()
        {
            var tenantId = GetTenantId();
            var violations = await violationService.GetViolationsAsync(tenantId);
            return Ok(violations);
        }

        [HttpPost]
        public async Task<ActionResult<ViolationResponse>> CreateViolation([FromBody] ViolationRequest request)
        {
            var tenantId = GetTenantId();
            request.TenantId = tenantId;
            var violation = await violationService.CreateViolationAsync(request);
            return CreatedAtAction(nameof(GetViolation), new { id = violation.Id }, violation);
        }

        [HttpGet("analytics")]
        public async Task<ActionResult<AlphaSurveilance.DTOs.Responses.AnalyticsResponse>> GetAnalytics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? cameraId = null)
        {
            var tenantId = GetTenantId();
            var analytics = await violationService.GetAnalyticsAsync(tenantId, startDate, endDate, cameraId);
            return Ok(analytics);
        }

        [HttpGet("stats")]
        public async Task<ActionResult<ViolationStatsResponse>> GetStats()
        {
            var tenantId = GetTenantId();
            var stats = await violationService.GetStatsAsync(tenantId);
            return Ok(stats);
        }

        /// <summary>
        /// [SERVICE-TO-SERVICE] Accepts violations directly from the Vision Inference Service.
        /// Protected by X-Internal-Api-Key middleware — NOT JWT.
        /// Writes directly to the DB, bypassing SQS entirely.
        /// Zero AWS cost — violations appear on the frontend immediately.
        /// </summary>
        [HttpPost("internal")]
        [AllowAnonymous] // Auth handled by InternalApiKeyMiddleware before this point
        public async Task<IActionResult> PostViolationsInternal([FromBody] List<ViolationPayload> payloads)
        {
            if (payloads == null || payloads.Count == 0)
                return BadRequest(new { error = "Empty payload" });

            // Add explicit PIPELINE logging to track incoming payloads from Vision Inference
            var logger = HttpContext.RequestServices.GetService<ILogger<ViolationsController>>();
            logger?.LogInformation("[PIPELINE] Received {Count} violation(s) from Vision Inference Service.", payloads.Count);
            foreach (var payload in payloads)
            {
                logger?.LogInformation("[PIPELINE] Incoming payload for CameraId: {CameraId}, TenantId: {TenantId}, Timestamp: {Timestamp}", payload.CameraId, payload.TenantId, payload.Timestamp);
            }

            var count = await violationService.ProcessViolationsBulkAsync(payloads);
            return Ok(new { processed = count });
        }
    }
}
