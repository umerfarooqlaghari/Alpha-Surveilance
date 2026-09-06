using System;
using System.Threading.Tasks;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces
{
    public interface IReliefAnalyticsService
    {
        Task<ReliefAnalyticsSummaryResponse> GetAnalyticsSummaryAsync(
            Guid tenantId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            Guid? locationId = null);
    }
}
