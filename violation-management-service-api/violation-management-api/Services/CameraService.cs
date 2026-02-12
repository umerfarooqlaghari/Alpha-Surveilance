using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class CameraService : ICameraService
{
    private readonly AppViolationDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<CameraService> _logger;

    public CameraService(
        AppViolationDbContext context,
        IEncryptionService encryptionService,
        ILogger<CameraService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<CameraResponse> CreateCameraAsync(CreateCameraRequest request)
    {
        // Check if CameraId already exists
        if (await _context.Cameras.AnyAsync(c => c.CameraId == request.CameraId))
        {
            throw new InvalidOperationException($"Camera with ID '{request.CameraId}' already exists");
        }

        // Validate tenant exists
        var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId);
        if (!tenantExists)
        {
            throw new InvalidOperationException($"Tenant with ID '{request.TenantId}' not found");
        }

        var camera = new Camera
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            CameraId = request.CameraId,
            Name = request.Name,
            Location = request.Location,
            RtspUrlEncrypted = _encryptionService.Encrypt(request.RtspUrl),
            Status = CameraStatus.Active,
            EnableSafetyViolations = request.EnableSafetyViolations,
            EnableSecurityViolations = request.EnableSecurityViolations,
            EnableOperationalViolations = request.EnableOperationalViolations,
            EnableComplianceViolations = request.EnableComplianceViolations,
            CreatedAt = DateTime.UtcNow
        };

        _context.Cameras.Add(camera);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created camera {CameraId} for tenant {TenantId}", camera.CameraId, camera.TenantId);

        var createdCamera = await _context.Cameras
            .Include(c => c.Tenant)
            .FirstAsync(c => c.Id == camera.Id);

        return CameraResponse.FromEntity(createdCamera);
    }

    public async Task<List<CameraResponse>> GetCamerasByTenantAsync(Guid tenantId)
    {
        var cameras = await _context.Cameras
            .Include(c => c.Tenant)
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return cameras.Select(CameraResponse.FromEntity).ToList();
    }

    public async Task<CameraResponse?> GetCameraByIdAsync(Guid id)
    {
        var camera = await _context.Cameras
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == id);

        return camera == null ? null : CameraResponse.FromEntity(camera);
    }

    public async Task<string?> GetDecryptedRtspUrlAsync(Guid id)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return null;

        try
        {
            return _encryptionService.Decrypt(camera.RtspUrlEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting RTSP URL for camera {CameraId}", id);
            throw;
        }
    }

    public async Task<CameraResponse?> UpdateCameraAsync(Guid id, UpdateCameraRequest request)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return null;

        if (!string.IsNullOrEmpty(request.Name))
            camera.Name = request.Name;

        if (!string.IsNullOrEmpty(request.Location))
            camera.Location = request.Location;

        if (!string.IsNullOrEmpty(request.RtspUrl))
            camera.RtspUrlEncrypted = _encryptionService.Encrypt(request.RtspUrl);

        if (request.EnableSafetyViolations.HasValue)
            camera.EnableSafetyViolations = request.EnableSafetyViolations.Value;

        if (request.EnableSecurityViolations.HasValue)
            camera.EnableSecurityViolations = request.EnableSecurityViolations.Value;

        if (request.EnableOperationalViolations.HasValue)
            camera.EnableOperationalViolations = request.EnableOperationalViolations.Value;

        if (request.EnableComplianceViolations.HasValue)
            camera.EnableComplianceViolations = request.EnableComplianceViolations.Value;

        camera.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated camera {CameraId}", id);

        var updatedCamera = await _context.Cameras
            .Include(c => c.Tenant)
            .FirstAsync(c => c.Id == id);

        return CameraResponse.FromEntity(updatedCamera);
    }

    public async Task<CameraResponse?> UpdateCameraStatusAsync(Guid id, CameraStatus status)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return null;

        camera.Status = status;
        camera.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated camera {CameraId} status to {Status}", id, status);

        var updatedCamera = await _context.Cameras
            .Include(c => c.Tenant)
            .FirstAsync(c => c.Id == id);

        return CameraResponse.FromEntity(updatedCamera);
    }

    public async Task<bool> DeleteCameraAsync(Guid id)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return false;

        _context.Cameras.Remove(camera);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted camera {CameraId}", id);

        return true;
    }
}
