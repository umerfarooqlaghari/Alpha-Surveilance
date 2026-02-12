using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface ITenantService
{
    Task<TenantResponse> CreateTenantAsync(CreateTenantRequest request);
    Task<TenantListResponse> GetAllTenantsAsync(int pageNumber = 1, int pageSize = 10);
    Task<TenantResponse?> GetTenantByIdAsync(Guid id);
    Task<TenantResponse?> GetTenantBySlugAsync(string slug);
    Task<TenantResponse?> UpdateTenantAsync(Guid id, UpdateTenantRequest request);
    Task<TenantResponse?> UpdateTenantStatusAsync(Guid id, TenantStatus status);
    Task<(string Url, string PublicId)?> UploadTenantLogoAsync(Guid id, IFormFile file);
    Task<bool> DeleteTenantAsync(Guid id);
}
