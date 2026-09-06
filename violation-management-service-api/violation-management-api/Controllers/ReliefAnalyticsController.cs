using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AlphaSurveilance.Services.Interfaces;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReliefAnalyticsController : ControllerBase
    {
        private readonly IReliefAnalyticsService _reliefAnalyticsService;
        private readonly ICurrentTenantService _currentTenantService;
        private readonly ILogger<ReliefAnalyticsController> _logger;

        public ReliefAnalyticsController(
            IReliefAnalyticsService reliefAnalyticsService,
            ICurrentTenantService currentTenantService,
            ILogger<ReliefAnalyticsController> logger)
        {
            _reliefAnalyticsService = reliefAnalyticsService;
            _currentTenantService = currentTenantService;
            _logger = logger;
        }

        [HttpGet("summary")]
        public async Task<ActionResult<ReliefAnalyticsSummaryResponse>> GetSummary(
            [FromQuery] Guid? tenantId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] Guid? locationId)
        {
            var effectiveTenantId = ResolveTenantId(tenantId);
            if (effectiveTenantId == Guid.Empty) return BadRequest("TenantId is required.");

            try
            {
                var summary = await _reliefAnalyticsService.GetAnalyticsSummaryAsync(
                    effectiveTenantId, startDate, endDate, locationId);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing relief analytics summary for tenant {TenantId}", effectiveTenantId);
                return StatusCode(500, new { error = ex.Message });
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
