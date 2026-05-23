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

        /// <summary>Returns only the violations flagged as false-positive for the current tenant.</summary>
        [HttpGet("false-positives")]
        public async Task<ActionResult<IEnumerable<ViolationResponse>>> GetFalsePositiveViolations()
        {
            var tenantId = GetTenantId();
            var violations = await violationService.GetFalsePositiveViolationsAsync(tenantId);
            return Ok(violations);
        }

        /// <summary>Bulk-flags one or more violations as false-positive.</summary>
        [HttpPost("false-positives/mark")]
        public async Task<IActionResult> MarkFalsePositive([FromBody] AlphaSurveilance.DTOs.Requests.MarkFalsePositiveRequest request)
        {
            if (request?.ViolationIds == null || request.ViolationIds.Count == 0)
                return BadRequest(new { error = "ViolationIds is required" });

            var tenantId = GetTenantId();
            var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User?.FindFirst("sub")?.Value
                         ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            var affected = await violationService.MarkFalsePositiveAsync(request.ViolationIds, tenantId, userId, request.Reason);
            return Ok(new { marked = affected });
        }

        /// <summary>Bulk-restores false-positive violations back to the active list.</summary>
        [HttpPost("false-positives/unmark")]
        public async Task<IActionResult> UnmarkFalsePositive([FromBody] AlphaSurveilance.DTOs.Requests.UnmarkFalsePositiveRequest request)
        {
            if (request?.ViolationIds == null || request.ViolationIds.Count == 0)
                return BadRequest(new { error = "ViolationIds is required" });

            var tenantId = GetTenantId();
            var affected = await violationService.UnmarkFalsePositiveAsync(request.ViolationIds, tenantId);
            return Ok(new { unmarked = affected });
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
            [FromQuery] string? cameraId = null,
            [FromQuery] Guid? locationId = null)
        {
            var tenantId = GetTenantId();
            var analytics = await violationService.GetAnalyticsAsync(tenantId, startDate, endDate, cameraId, locationId);
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
