using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces
{
    public interface IWorkstationService
    {
        Task<List<WorkstationResponse>> GetWorkstationsAsync(Guid tenantId, Guid? locationId = null, Guid? cameraId = null);
        Task<WorkstationDetailResponse?> GetWorkstationByIdAsync(Guid tenantId, Guid id);
        Task<WorkstationResponse> CreateWorkstationAsync(Guid tenantId, CreateWorkstationRequest request);
        Task<WorkstationResponse?> UpdateWorkstationAsync(Guid tenantId, Guid id, UpdateWorkstationRequest request);
        Task<bool> DeleteWorkstationAsync(Guid tenantId, Guid id);
        Task<List<WorkstationAssignmentResponse>> AssignWorkersAsync(Guid tenantId, Guid workstationId, AssignWorkstationWorkersRequest request);
    }
}
