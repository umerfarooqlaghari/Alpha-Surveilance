using System.Security.Claims;

namespace violation_management_api.Services.Interfaces;

public interface IJwtService
{
    string GenerateToken(Guid userId, string email, string role, Guid? tenantId);
    ClaimsPrincipal? ValidateToken(string token);
    Guid? GetUserIdFromToken(string token);
    string? GetRoleFromToken(string token);
    Guid? GetTenantIdFromToken(string token);
}
