using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface IUserService
{
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);
    Task<List<UserResponse>> GetUsersByTenantAsync(Guid? tenantId);
    Task<UserResponse?> GetUserByIdAsync(Guid id);
    Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request);
    Task<bool> ResetPasswordAsync(Guid id, string newPassword);
    Task<bool> ToggleUserStatusAsync(Guid id);
    Task<bool> DeleteUserAsync(Guid id);
}
