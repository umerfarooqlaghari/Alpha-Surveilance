using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface ISopService
{
    Task<SopResponse> CreateSopAsync(CreateSopRequest request);
    Task<List<SopResponse>> GetAllSopsAsync();
    Task<SopResponse?> GetSopByIdAsync(Guid id);
    Task<SopResponse?> UpdateSopAsync(Guid id, UpdateSopRequest request);
    Task<bool> DeleteSopAsync(Guid id);

    Task<SopViolationTypeResponse> CreateSopViolationTypeAsync(Guid sopId, CreateSopViolationTypeRequest request);
    Task<SopViolationTypeResponse?> UpdateSopViolationTypeAsync(Guid id, UpdateSopViolationTypeRequest request);
    Task<bool> DeleteSopViolationTypeAsync(Guid id);
}
