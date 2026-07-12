using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;

namespace AlphaSurveilance.Services.Interfaces
{
    public interface IViolationService
    {
        Task<ViolationResponse?> GetViolationAsync(Guid id, string tenantId);

        /// <summary>
        /// Tenant-scoped violation list, newest first. Optional paging: when
        /// <paramref name="limit"/>/<paramref name="offset"/> are omitted the
        /// historical unbounded behaviour is preserved.
        /// </summary>
        Task<IEnumerable<ViolationResponse>> GetViolationsAsync(string tenantId, int? limit = null, int? offset = null);

        /// <summary>
        /// [INTERNAL] Most recent non-resolved violation for a (cameraId, trackId)
        /// pair. Serves GET /api/violations/internal/active for the vision service.
        /// </summary>
        Task<ViolationResponse?> GetActiveViolationAsync(string cameraId, long trackId);

        /// <summary>
        /// [INTERNAL] Updates LastSeenAt (and optionally Status) of an existing
        /// violation. Serves PATCH /api/violations/internal/{id}. Returns false
        /// when the violation does not exist.
        /// </summary>
        Task<bool> UpdateViolationLifecycleAsync(Guid id, InternalViolationUpdateRequest request);
        Task<IEnumerable<ViolationResponse>> GetFalsePositiveViolationsAsync(string tenantId);
        Task<ViolationResponse> CreateViolationAsync(ViolationRequest request);
        Task<bool> ProcessViolationAsync(ViolationRequest request);
        Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationRequest> requests);
        Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationPayload> requests);
        Task<AlphaSurveilance.DTOs.Responses.AnalyticsResponse> GetAnalyticsAsync(string tenantId, DateTime? startDate = null, DateTime? endDate = null, string? cameraId = null, Guid? locationId = null);
        Task<ViolationStatsResponse> GetStatsAsync(string tenantId);

        // Bulk mark / unmark false-positive. Returns number of rows affected.
        Task<int> MarkFalsePositiveAsync(IEnumerable<Guid> ids, string tenantId, string? userId, string? reason);
        Task<int> UnmarkFalsePositiveAsync(IEnumerable<Guid> ids, string tenantId);
    }
}
