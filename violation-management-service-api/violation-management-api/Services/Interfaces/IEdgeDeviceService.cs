using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface IEdgeDeviceService
{
    /// <summary>
    /// Idempotent: returns existing device with matching (TenantId, DeviceIdentifier)
    /// or creates a new one. Used by the vision service on startup.
    /// </summary>
    Task<(EdgeDeviceResponse Device, bool IsNew)> RegisterAsync(RegisterDeviceRequest request);

    /// <summary>Updates LastSeenAt to now. No-op if device is missing or deleted.</summary>
    Task<bool> RecordHeartbeatAsync(Guid deviceId);

    Task<EdgeDeviceResponse> CreateAsync(CreateEdgeDeviceRequest request);
    Task<List<EdgeDeviceResponse>> GetByTenantAsync(Guid tenantId);
    Task<List<EdgeDeviceResponse>> GetAllAsync();
    Task<EdgeDeviceResponse?> GetByIdAsync(Guid id);
    Task<EdgeDeviceResponse?> UpdateAsync(Guid id, UpdateEdgeDeviceRequest request);
    Task<bool> DeleteAsync(Guid id);

    Task<bool> AssignCameraAsync(Guid deviceId, Guid cameraId);
    Task<bool> UnassignCameraAsync(Guid deviceId, Guid cameraId);
}
