using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface ICameraService
{
    Task<CameraResponse> CreateCameraAsync(CreateCameraRequest request);
    Task<List<CameraResponse>> GetCamerasByTenantAsync(Guid tenantId);
    Task<CameraResponse?> GetCameraByIdAsync(Guid id);
    Task<string?> GetDecryptedRtspUrlAsync(Guid id);
    Task<CameraResponse?> UpdateCameraAsync(Guid id, UpdateCameraRequest request);
    Task<CameraResponse?> UpdateCameraStatusAsync(Guid id, CameraStatus status);
    Task<bool> DeleteCameraAsync(Guid id);
}
