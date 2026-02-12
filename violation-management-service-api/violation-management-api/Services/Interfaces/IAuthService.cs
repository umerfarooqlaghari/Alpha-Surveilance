using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> AuthenticateSuperAdminAsync(SuperAdminLoginRequest request);
    Task<AuthResponse> AuthenticateTenantAdminAsync(TenantAdminLoginRequest request);
    Task<bool> ValidateTokenAsync(string token);
}
