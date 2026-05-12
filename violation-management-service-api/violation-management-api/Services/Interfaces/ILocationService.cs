using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface ILocationService
{
    Task<LocationResponse> CreateLocationAsync(CreateLocationRequest request);
    Task<List<LocationResponse>> GetLocationsByTenantAsync(Guid tenantId, string? search = null);
    Task<LocationResponse?> GetLocationByIdAsync(Guid id);
    Task<LocationResponse?> UpdateLocationAsync(Guid id, UpdateLocationRequest request);
    Task<bool> DeleteLocationAsync(Guid id);
}
