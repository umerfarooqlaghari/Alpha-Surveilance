using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces
{
    public interface IReliefTrackingService
    {
        Task<ReliefSessionResponse?> ProcessReliefEventAsync(ReliefEventIngestRequest request);
        Task<List<ReliefSessionResponse>> GetSessionsAsync(
            Guid tenantId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            Guid? workstationId = null,
            string? status = null,
            string? employeeExternalId = null);
        Task<List<WorkstationLiveBoardItemResponse>> GetLiveBoardAsync(Guid tenantId, Guid? locationId = null);
    }
}
