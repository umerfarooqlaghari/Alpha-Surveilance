using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface IAiModelService
{
    Task<List<AiModelResponse>> GetAllAsync();
    Task<AiModelResponse?> GetByIdAsync(Guid id);
    Task<AiModelResponse> RegisterAsync(RegisterAiModelRequest request);
    Task<AiModelResponse?> UpdateAsync(Guid id, RegisterAiModelRequest request);
    Task<bool> EnableAsync(Guid id);
    Task<bool> DisableAsync(Guid id);
    Task<(bool Success, string? Error)> DeleteAsync(Guid id);
    Task<bool> UpdateEdgeStatusAsync(Guid id, EdgeModelStatusUpdate update);
}
