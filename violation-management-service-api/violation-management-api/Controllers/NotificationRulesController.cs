using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using violation_management_api.Core.Entities;
using AlphaSurveilance.Services.Interfaces;
using System.Text.Json;

namespace AlphaSurveilance.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationRulesController : ControllerBase
    {
        private readonly AppViolationDbContext _dbContext;
        private readonly ICurrentTenantService _currentTenantService;

        public NotificationRulesController(AppViolationDbContext dbContext, ICurrentTenantService currentTenantService)
        {
            _dbContext = dbContext;
            _currentTenantService = currentTenantService;
        }

        private Guid GetTenantId()
        {
            var tenantId = _currentTenantService.TenantId;
            if (!tenantId.HasValue)
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            return tenantId.Value;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<NotificationRuleResponse>>> GetRules()
        {
            var tenantId = GetTenantId();
            var rules = await _dbContext.NotificationRules
                .Where(r => r.TenantId == tenantId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var responses = rules.Select(r => new NotificationRuleResponse
            {
                Id = r.Id,
                Name = r.Name,
                TargetEmails = JsonSerializer.Deserialize<List<string>>(r.TargetEmailsJson) ?? new List<string>(),
                FilterLocationIds = JsonSerializer.Deserialize<List<Guid>>(r.FilterLocationIdsJson) ?? new List<Guid>(),
                FilterCameraIds = JsonSerializer.Deserialize<List<string>>(r.FilterCameraIdsJson) ?? new List<string>(),
                FilterViolationTypeIds = JsonSerializer.Deserialize<List<Guid>>(r.FilterViolationTypeIdsJson) ?? new List<Guid>(),
                FilterSeverities = JsonSerializer.Deserialize<List<string>>(r.FilterSeveritiesJson) ?? new List<string>(),
                FilterDepartments = JsonSerializer.Deserialize<List<string>>(r.FilterDepartmentsJson) ?? new List<string>(),
                TimeIntervals = JsonSerializer.Deserialize<List<TimeIntervalDto>>(r.TimeIntervalsJson) ?? new List<TimeIntervalDto>(),
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt
            });

            return Ok(responses);
        }

        [HttpPost]
        public async Task<ActionResult<NotificationRuleResponse>> CreateRule([FromBody] NotificationRuleRequest request)
        {
            var tenantId = GetTenantId();

            var rule = new NotificationRule
            {
                TenantId = tenantId,
                Name = request.Name,
                TargetEmailsJson = JsonSerializer.Serialize(request.TargetEmails),
                FilterLocationIdsJson = JsonSerializer.Serialize(request.FilterLocationIds),
                FilterCameraIdsJson = JsonSerializer.Serialize(request.FilterCameraIds),
                FilterViolationTypeIdsJson = JsonSerializer.Serialize(request.FilterViolationTypeIds),
                FilterSeveritiesJson = JsonSerializer.Serialize(request.FilterSeverities),
                FilterDepartmentsJson = JsonSerializer.Serialize(request.FilterDepartments),
                TimeIntervalsJson = JsonSerializer.Serialize(request.TimeIntervals),
                IsActive = request.IsActive
            };

            _dbContext.NotificationRules.Add(rule);
            await _dbContext.SaveChangesAsync();

            return Ok(new NotificationRuleResponse
            {
                Id = rule.Id,
                Name = rule.Name,
                TargetEmails = request.TargetEmails,
                FilterLocationIds = request.FilterLocationIds,
                FilterCameraIds = request.FilterCameraIds,
                FilterViolationTypeIds = request.FilterViolationTypeIds,
                FilterSeverities = request.FilterSeverities,
                FilterDepartments = request.FilterDepartments,
                TimeIntervals = request.TimeIntervals,
                IsActive = rule.IsActive,
                CreatedAt = rule.CreatedAt
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRule(Guid id, [FromBody] NotificationRuleRequest request)
        {
            var tenantId = GetTenantId();
            var rule = await _dbContext.NotificationRules.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);

            if (rule == null) return NotFound();

            rule.Name = request.Name;
            rule.TargetEmailsJson = JsonSerializer.Serialize(request.TargetEmails);
            rule.FilterLocationIdsJson = JsonSerializer.Serialize(request.FilterLocationIds);
            rule.FilterCameraIdsJson = JsonSerializer.Serialize(request.FilterCameraIds);
            rule.FilterViolationTypeIdsJson = JsonSerializer.Serialize(request.FilterViolationTypeIds);
            rule.FilterSeveritiesJson = JsonSerializer.Serialize(request.FilterSeverities);
            rule.FilterDepartmentsJson = JsonSerializer.Serialize(request.FilterDepartments);
            rule.TimeIntervalsJson = JsonSerializer.Serialize(request.TimeIntervals);
            rule.IsActive = request.IsActive;

            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRule(Guid id)
        {
            var tenantId = GetTenantId();
            var rule = await _dbContext.NotificationRules.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);

            if (rule == null) return NotFound();

            _dbContext.NotificationRules.Remove(rule);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
    }
}
