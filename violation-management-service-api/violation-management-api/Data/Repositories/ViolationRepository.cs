using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AlphaSurveilance.Data.Repositories
{
    public class ViolationRepository(AppViolationDbContext dbContext) : IViolationRepository
    {
        public async Task<Violation?> GetByIdAsync(Guid id, string tenantId)
        {
            // Strict tenant isolation at the query level
            return await dbContext.Violations
                .FirstOrDefaultAsync(v => v.Id == id && v.TenantId == tenantId);
        }

        public async Task<IEnumerable<Violation>> GetAllAsync(string tenantId)
        {
            return await dbContext.Violations
                .Where(v => v.TenantId == tenantId)
                .OrderByDescending(v => v.Timestamp)
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
            return await dbContext.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.RetryCount < 50)
                .OrderBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync();
        }

        public async Task UpdateOutboxMessage(OutboxMessage message)
        {
            dbContext.OutboxMessages.Update(message);
            await Task.CompletedTask;
        }
        public async Task<(int ActiveViolations, int ResolvedToday)> GetStatsAsync(string tenantId)
        {
            var today = DateTime.UtcNow.Date;
            
            var activeViolations = await dbContext.Violations
                .CountAsync(v => v.TenantId == tenantId && v.Status == Core.Enums.AuditStatus.Pending);

            // Assuming "Resolved Today" means violations that are Audited and occurred/were created today
            // Ideally we'd have an AuditedAt timestamp, but using CreatedAt/Timestamp for now as proxy
            var resolvedToday = await dbContext.Violations
                .CountAsync(v => v.TenantId == tenantId && 
                                 v.Status == Core.Enums.AuditStatus.Audited && 
                                 v.CreatedAt >= today);

            return (activeViolations, resolvedToday);
        }
    }
}
