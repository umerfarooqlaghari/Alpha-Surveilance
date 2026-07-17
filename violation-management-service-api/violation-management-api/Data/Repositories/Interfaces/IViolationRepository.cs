using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;

namespace AlphaSurveilance.Data.Repositories.Interfaces
{
    public interface IViolationRepository
    {
        Task<Violation?> GetByIdAsync(Guid id, Guid tenantId);

        /// <summary>
        /// [INTERNAL] Cross-tenant lookup by primary key. Only for the
        /// X-Internal-Api-Key protected service-to-service endpoints — never
        /// expose through a JWT/tenant-scoped route.
        /// </summary>
        Task<Violation?> GetByIdInternalAsync(Guid id);

        /// <summary>
        /// [INTERNAL] Most recent non-resolved (Pending, non-false-positive)
        /// violation for a (cameraId, trackId) pair, or null when none exists.
        /// </summary>
        Task<Violation?> GetActiveByTrackAsync(string cameraId, long trackId);

        Task<IEnumerable<Violation>> GetAllAsync(Guid tenantId, bool includeFalsePositives = false, int? limit = null, int? offset = null);
        Task<IEnumerable<Violation>> GetFalsePositivesAsync(Guid tenantId);
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
        Task<AlphaSurveilance.DTOs.Responses.AnalyticsResponse> GetAnalyticsAsync(Guid tenantId, DateTime? startDate = null, DateTime? endDate = null, string? cameraId = null, Guid? locationId = null);

        // False-positive bulk mutations (issue a single UPDATE per call via ExecuteUpdateAsync).
        Task<int> MarkFalsePositiveAsync(IEnumerable<Guid> ids, Guid tenantId, string? userId, string? reason);
        Task<int> UnmarkFalsePositiveAsync(IEnumerable<Guid> ids, Guid tenantId);
    }
}
