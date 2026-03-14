using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using violation_management_api.Core.Entities;

namespace AlphaSurveilance.Data.Repositories
{
    public class ViolationRepository(AppViolationDbContext dbContext, Microsoft.Extensions.Configuration.IConfiguration config) : IViolationRepository
    {
        public async Task<Violation?> GetByIdAsync(Guid id, Guid tenantId)
        {
            // Strict tenant isolation at the query level
            return await dbContext.Violations
                .FirstOrDefaultAsync(v => v.Id == id && v.TenantId == tenantId);
        }

        public async Task<IEnumerable<Violation>> GetAllAsync(Guid tenantId)
        {
            return await dbContext.Violations
                .Include(v => v.SopViolationType)
                .ThenInclude(sv => sv!.Sop)
                .Where(v => v.TenantId == tenantId)
                .OrderByDescending(v => v.Timestamp)
                .Take(50) // Limit for performance on dashboard
                .ToListAsync();
        }

        public async Task AddAsync(Violation violation)
        {
            await dbContext.Violations.AddAsync(violation);
        }

        public async Task AddRangeAsync(IEnumerable<Violation> violations)
        {
            await dbContext.Violations.AddRangeAsync(violations);
        }

        public async Task UpdateAsync(Violation violation)
        {
            dbContext.Violations.Update(violation);
            await Task.CompletedTask;
        }

        public async Task<bool> ExistsByCorrelationIdAsync(string correlationId)
        {
            return await dbContext.Violations.AnyAsync(v => v.CorrelationId == correlationId);
        }

        public async Task<IEnumerable<string>> GetExistingCorrelationIdsAsync(IEnumerable<string> correlationIds)
        {
            return await dbContext.Violations
                .Where(v => correlationIds.Contains(v.CorrelationId))
                .Select(v => v.CorrelationId)
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await dbContext.SaveChangesAsync();
        }

        public async Task AddOutboxMessagesAsync(IEnumerable<OutboxMessage> messages)
        {
            await dbContext.OutboxMessages.AddRangeAsync(messages);
        }

        public async Task<IEnumerable<OutboxMessage>> GetUnprocessedOutboxMessagesAsync(int batchSize)
        {
            var maxRetries = config.GetValue<int>("OutboxConfig:MaxRetryCount", 5);
            var cooldownHours = config.GetValue<int>("OutboxConfig:RetryCooldownHours", 1);
            var cutoff = DateTime.UtcNow.AddHours(-cooldownHours);

            return await dbContext.OutboxMessages
                .Where(m => m.ProcessedAt == null && 
                            m.RetryCount < maxRetries && 
                            (m.LastAttemptAt == null || m.LastAttemptAt < cutoff))
                .OrderBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync();
        }

        public async Task UpdateOutboxMessage(OutboxMessage message)
        {
            dbContext.OutboxMessages.Update(message);
            await Task.CompletedTask;
        }
        public async Task<(int ActiveViolations, int ResolvedToday)> GetStatsAsync(Guid tenantId)
        {
            var todayUtc = DateTime.UtcNow.Date;
            
            var activeViolations = await dbContext.Violations
                .CountAsync(v => v.TenantId == tenantId && v.Status == Core.Enums.AuditStatus.Pending);

            // "Resolved Today" = violations with an audit record that was submitted/reviewed today
            // We join ViolationAudits so we count by WHEN the audit was done, not when the violation was detected.
            var resolvedToday = await dbContext.ViolationAudits
                .Where(a => a.TenantId == tenantId
                         && a.Status >= AuditRecordStatus.Submitted   // Draft doesn't count
                         && a.UpdatedAt >= todayUtc)
                .CountAsync();

            return (activeViolations, resolvedToday);
        }

        public async Task<AlphaSurveilance.DTOs.Responses.AnalyticsResponse> GetAnalyticsAsync(Guid tenantId, DateTime? startDate = null, DateTime? endDate = null, string? cameraId = null)
        {
            var response = new AlphaSurveilance.DTOs.Responses.AnalyticsResponse();
            
            // Base Query for filtered stats
            var query = dbContext.Violations.Where(v => v.TenantId == tenantId);

            if (startDate.HasValue)
                query = query.Where(v => v.Timestamp >= startDate.Value);
            
            if (endDate.HasValue)
                query = query.Where(v => v.Timestamp <= endDate.Value);

            if (!string.IsNullOrEmpty(cameraId))
            {
                // Violations store Camera.Id (Guid) as string in CameraId field.
                // Frontend sends Camera.CameraId (user-friendly string) from the dropdown.
                // Resolve the Guid primary key first — compare c.CameraId (string) then use c.Id.
                var cam = await dbContext.Cameras
                    .Where(c => c.TenantId == tenantId && c.CameraId == cameraId)
                    .Select(c => c.Id)          // fetch Guid directly — EF Core can translate this
                    .FirstOrDefaultAsync();

                // cam == Guid.Empty means not found — fall back to cameraId as-is (might already be a Guid string)
                var resolvedCameraGuid = cam != Guid.Empty
                    ? cam.ToString()
                    : cameraId;

                query = query.Where(v => v.CameraId == resolvedCameraGuid);
            }

            // 1. Summary Stats (Total respects filters, but Active/Resolved/Critical might need to respect them too?)
            // Usually summary stats on a dashboard might be "Status right now" vs "Historical view". 
            // If filters are applied, the summary should probably reflect the filtered dataset.
            
            response.Summary.TotalViolations = await query.CountAsync();
            response.Summary.ActiveViolations = await query.CountAsync(v => v.Status == Core.Enums.AuditStatus.Pending);
            response.Summary.ResolvedViolations = await query.CountAsync(v => v.Status == Core.Enums.AuditStatus.Audited);
            response.Summary.CriticalViolations = 0;

            // 2. Daily Trends
            // If date range is huge, grouping by day is fine.
            var trends = await query
                .GroupBy(v => v.Timestamp.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync();

            response.DailyTrends = trends.Select(t => new AlphaSurveilance.DTOs.Responses.TrendData { Date = t.Date, Count = t.Count }).ToList();

            response.ByCategory = new List<AlphaSurveilance.DTOs.Responses.CategoryData>();

            // 4. By Severity
            response.BySeverity = new List<AlphaSurveilance.DTOs.Responses.SeverityData>();

            // 5. Hourly Heatmap
            // Optimization: Fetch only Timestamp.Hour if possible, or fetch timestamps and group in memory if dataset is small
            // Given filters, dataset is likely smaller.
            var timestamps = await query
                .Select(v => v.Timestamp.Hour)
                .ToListAsync();

            var heatmap = timestamps
                .GroupBy(h => h)
                .Select(g => new AlphaSurveilance.DTOs.Responses.HeatmapData { Hour = g.Key, Count = g.Count() })
                .OrderBy(h => h.Hour)
                .ToList();
            
            response.HourlyHeatmap = heatmap;

            // 6. By Camera (Top 10)
            // Violations store Camera.Id (Guid) as string in CameraId field.
            // We join against Camera.Id (Guid), not Camera.CameraId (string).
            
            var byCamera = await query
                .GroupBy(v => v.CameraId)
                .Select(g => new { CameraId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();
            
            // Parse the stored CameraId strings back to Guids for EF Core comparison
            // (EF Core cannot translate Guid.ToString() inside WHERE/Contains to SQL)
            var cameraGuids = byCamera
                .Select(x => x.CameraId)
                .Where(id => Guid.TryParse(id, out _))
                .Select(Guid.Parse)
                .Distinct()
                .ToList();

            // Fetch camera names by Guid primary key — fully EF Core translatable
            var camerasById = await dbContext.Cameras
                .Where(c => c.TenantId == tenantId && cameraGuids.Contains(c.Id))
                .Select(c => new { Id = c.Id.ToString(), c.Name })
                .ToListAsync();
            
            // Note: the .Select projection c.Id.ToString() runs CLIENT-SIDE after materialization
            // because EasternFramework materializes plain struct projections before applying ToString()
            response.ByCamera = byCamera.Select(x => new AlphaSurveilance.DTOs.Responses.CameraData 
            { 
                CameraName = camerasById.FirstOrDefault(c => c.Id.Equals(x.CameraId, StringComparison.OrdinalIgnoreCase))?.Name 
                             ?? x.CameraId ?? "Unknown", 
                Count = x.Count 
            }).ToList();

            return response;
        }
    }
}
