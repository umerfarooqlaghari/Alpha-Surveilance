using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Core.Entities;

namespace violation_management_api.Services.Interfaces;

public interface ITenantViolationRequestService
{
    Task<TenantViolationRequestResponse> CreateRequestAsync(Guid tenantId, CreateTenantViolationRequestDto request);
    Task<List<TenantViolationRequestResponse>> GetRequestsByTenantAsync(Guid tenantId);
    Task<List<TenantViolationRequestResponse>> GetApprovedRequestsByTenantAsync(Guid tenantId);
    Task<List<TenantViolationRequestResponse>> GetAllPendingRequestsAsync();
    Task<TenantViolationRequestResponse?> ResolveRequestAsync(Guid requestId, RequestStatus status);
    Task<TenantViolationRequestResponse> AssignProactiveRequestAsync(Guid tenantId, Guid violationTypeId);
    Task<List<TenantViolationRequestResponse>> GetAllRequestsAsync();
    Task<bool> UnassignAsync(Guid requestId);
}
