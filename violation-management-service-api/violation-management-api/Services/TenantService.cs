using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class TenantService : ITenantService
{
    private readonly AppViolationDbContext _context;
    private readonly ICloudinaryService _cloudinaryService;
    private readonly ILogger<TenantService> _logger;

    public TenantService(
        AppViolationDbContext context,
        ICloudinaryService cloudinaryService,
        ILogger<TenantService> logger)
    {
        _context = context;
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    public async Task<TenantResponse> CreateTenantAsync(CreateTenantRequest request)
    {
        // Check if slug already exists
        if (await _context.Tenants.AnyAsync(t => t.Slug == request.Slug))
        {
            throw new InvalidOperationException($"Tenant with slug '{request.Slug}' already exists");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantName = request.TenantName,
            Slug = request.Slug,
            EmployeeCount = request.EmployeeCount,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            Industry = request.Industry,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created tenant {TenantId} with slug {Slug}", tenant.Id, tenant.Slug);

        return TenantResponse.FromEntity(tenant);
    }

    public async Task<TenantListResponse> GetAllTenantsAsync(int pageNumber = 1, int pageSize = 10)
    {
        var query = _context.Tenants
            .Include(t => t.Users)
            .Include(t => t.Cameras)
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var tenants = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new TenantListResponse
        {
            Tenants = tenants.Select(TenantResponse.FromEntity).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<TenantResponse?> GetTenantByIdAsync(Guid id)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Users)
            .Include(t => t.Cameras)
            .FirstOrDefaultAsync(t => t.Id == id);

        return tenant == null ? null : TenantResponse.FromEntity(tenant);
    }

    public async Task<TenantResponse?> GetTenantBySlugAsync(string slug)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Users)
            .Include(t => t.Cameras)
            .FirstOrDefaultAsync(t => t.Slug == slug);

        return tenant == null ? null : TenantResponse.FromEntity(tenant);
    }

    public async Task<TenantResponse?> UpdateTenantAsync(Guid id, UpdateTenantRequest request)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return null;

        if (!string.IsNullOrEmpty(request.TenantName))
            tenant.TenantName = request.TenantName;

        if (request.EmployeeCount.HasValue)
            tenant.EmployeeCount = request.EmployeeCount.Value;

        if (!string.IsNullOrEmpty(request.Address))
            tenant.Address = request.Address;

        if (!string.IsNullOrEmpty(request.City))
            tenant.City = request.City;

        if (!string.IsNullOrEmpty(request.Country))
            tenant.Country = request.Country;

        if (!string.IsNullOrEmpty(request.Industry))
            tenant.Industry = request.Industry;

        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated tenant {TenantId}", id);

        return TenantResponse.FromEntity(tenant);
    }

    public async Task<TenantResponse?> UpdateTenantStatusAsync(Guid id, TenantStatus status)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return null;

        tenant.Status = status;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated tenant {TenantId} status to {Status}", id, status);

        return TenantResponse.FromEntity(tenant);
    }

    public async Task<(string Url, string PublicId)?> UploadTenantLogoAsync(Guid id, IFormFile file)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return null;

        // Delete old logo if exists
        if (!string.IsNullOrEmpty(tenant.LogoPublicId))
        {
            await _cloudinaryService.DeleteImageAsync(tenant.LogoPublicId);
        }

        // Upload new logo
        var (url, publicId) = await _cloudinaryService.UploadImageAsync(file, "tenant-logos");

        tenant.LogoUrl = url;
        tenant.LogoPublicId = publicId;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Uploaded logo for tenant {TenantId}", id);

        return (url, publicId);
    }

    public async Task<bool> DeleteTenantAsync(Guid id)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return false;

        // Soft delete by setting status to Inactive
        tenant.Status = TenantStatus.Inactive;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Soft deleted tenant {TenantId}", id);

        return true;
    }
}
