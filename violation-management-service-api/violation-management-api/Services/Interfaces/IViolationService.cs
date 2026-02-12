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
        Task<IEnumerable<ViolationResponse>> GetViolationsAsync(string tenantId);
        Task<ViolationResponse> CreateViolationAsync(ViolationRequest request);
        Task<bool> ProcessViolationAsync(ViolationRequest request);
        Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationRequest> requests);
        Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationPayload> requests);
        Task<ViolationStatsResponse> GetStatsAsync(string tenantId);
    }
}
