using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;

namespace AlphaSurveilance.Data.Repositories.Interfaces
{
    public interface IViolationRepository
    {
        Task<Violation?> GetByIdAsync(Guid id, Guid tenantId);
        Task<IEnumerable<Violation>> GetAllAsync(Guid tenantId);
        Task AddAsync(Violation violation);
        Task AddRangeAsync(IEnumerable<Violation> violations);
        Task UpdateAsync(Violation violation);
        Task<bool> ExistsByCorrelationIdAsync(string correlationId);
        Task<IEnumerable<string>> GetExistingCorrelationIdsAsync(IEnumerable<string> correlationIds);
        Task SaveChangesAsync();

        // Outbox support
        Task AddOutboxMessagesAsync(IEnumerable<OutboxMessage> messages);
        Task<IEnumerable<OutboxMessage>> GetUnprocessedOutboxMessagesAsync(int batchSize);
        Task UpdateOutboxMessage(OutboxMessage message);
        // Stats support
        Task<(int ActiveViolations, int ResolvedToday)> GetStatsAsync(Guid tenantId);
        Task<AlphaSurveilance.DTOs.Responses.AnalyticsResponse> GetAnalyticsAsync(Guid tenantId, DateTime? startDate = null, DateTime? endDate = null, string? cameraId = null);
    }
}
