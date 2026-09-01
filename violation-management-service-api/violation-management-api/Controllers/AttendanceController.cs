using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services;
using AlphaSurveilance.Services.Interfaces;

namespace violation_management_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceService _attendanceService;
        private readonly ILogger<AttendanceController> _logger;
        private readonly ICurrentTenantService _currentTenantService;

        public AttendanceController(
            IAttendanceService attendanceService, 
            ILogger<AttendanceController> logger,
            ICurrentTenantService currentTenantService)
        {
            _attendanceService = attendanceService;
            _logger = logger;
            _currentTenantService = currentTenantService;
        }

        /// <summary>
        /// Internal webhook endpoint called by Vision Inference Service when a recognized employee is detected.
        /// </summary>
        [AllowAnonymous] // Auth handled by InternalApiKeyMiddleware before this point
        [HttpPost("internal/record")]
        public async Task<IActionResult> RecordAttendanceEvent([FromBody] AttendanceEventRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EmployeeExternalId))
            {
                return BadRequest("Invalid attendance request payload.");
            }

            try
            {
                var result = await _attendanceService.ProcessAttendanceEventAsync(request);
                if (result == null)
                {
                    return Ok(new { status = "Ignored", message = "Camera not configured for attendance or employee not found." });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing internal attendance event for employee {EmployeeId}", request.EmployeeExternalId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Query tenant attendance records with optional date range, location, status, and employee filters.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<AttendanceRecordResponse>>> GetAttendance(
            [FromQuery] Guid tenantId,
            [FromQuery] DateTime? shiftDate,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] Guid? locationId,
            [FromQuery] string? employeeId,
            [FromQuery] string? employeeExternalId,
            [FromQuery] string? status)
        {
            if (tenantId == Guid.Empty)
            {
                return BadRequest("tenantId is required.");
            }

            // Enforce tenant isolation
            if (!_currentTenantService.IsSuperAdmin)
            {
                var userTenantId = _currentTenantService.TenantId;
                if (!userTenantId.HasValue || userTenantId.Value != tenantId)
                {
                    return Forbid("Access denied to tenant attendance data.");
                }
            }

            var effectiveStartDate = startDate ?? shiftDate;
            var effectiveEndDate = endDate ?? shiftDate;
            var effectiveEmployeeId = !string.IsNullOrWhiteSpace(employeeExternalId) ? employeeExternalId : employeeId;

            var records = await _attendanceService.GetTenantAttendanceAsync(
                tenantId, effectiveStartDate, effectiveEndDate, locationId, effectiveEmployeeId, status);
            return Ok(records);
        }

        /// <summary>
        /// Get daily attendance summary metrics for dashboard.
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<AttendanceSummaryResponse>> GetAttendanceSummary(
            [FromQuery] Guid tenantId,
            [FromQuery] DateTime? shiftDate,
            [FromQuery] DateTime? date,
            [FromQuery] Guid? locationId)
        {
            if (tenantId == Guid.Empty)
            {
                return BadRequest("tenantId is required.");
            }

            // Enforce tenant isolation
            if (!_currentTenantService.IsSuperAdmin)
            {
                var userTenantId = _currentTenantService.TenantId;
                if (!userTenantId.HasValue || userTenantId.Value != tenantId)
                {
                    return Forbid("Access denied to tenant attendance summary.");
                }
            }

            var targetDate = date ?? shiftDate;
            var summary = await _attendanceService.GetAttendanceSummaryAsync(tenantId, targetDate, locationId);
            return Ok(summary);
        }
    }
}
