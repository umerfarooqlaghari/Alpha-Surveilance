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
    private readonly IConfiguration _configuration;
    private readonly ICloudflareService _cloudflareService;
    private static readonly HttpClient _httpClient = new HttpClient();

    public CameraService(
        AppViolationDbContext context,
        IEncryptionService encryptionService,
        ILogger<CameraService> logger,
        IConfiguration configuration,
        ICloudflareService cloudflareService)
    {
        _context = context;
        _encryptionService = encryptionService;
        _logger = logger;
        _configuration = configuration;
        _cloudflareService = cloudflareService;
    }

    private void TriggerVisionServiceReload()
    {
        var baseUrl = _configuration.GetValue<string>("VisionService:BaseUrl");
        if (string.IsNullOrEmpty(baseUrl)) return;
        
        // Fire-and-forget background task
        _ = Task.Run(async () => 
        {
            try
            {
                await _httpClient.PostAsync($"{baseUrl.TrimEnd('/')}/streams/reload", null);
                _logger.LogInformation("Successfully triggered Vision Service camera reload webhook");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to trigger Vision Service reload webhook — process may be down");
            }
        });
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

        if (request.ActiveViolations != null && request.ActiveViolations.Any())
        {
            var requestedIds = request.ActiveViolations.Select(v => v.SopViolationTypeId).ToList();
            var approvedViolationIds = await _context.TenantViolationRequests
                .Where(r => r.TenantId == request.TenantId && r.Status == RequestStatus.Approved)
                .Select(r => r.SopViolationTypeId)
                .ToListAsync();

            var unapprovedViolations = requestedIds.Except(approvedViolationIds).ToList();
            if (unapprovedViolations.Any())
            {
                throw new InvalidOperationException($"Cannot assign unapproved violation types: {string.Join(", ", unapprovedViolations)}");
            }
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
            TargetFps = request.TargetFps > 0 ? request.TargetFps : 1.0,
            ActiveViolationTypes = request.ActiveViolations?.Select(v => new CameraViolationType
            {
                 SopViolationTypeId = v.SopViolationTypeId,
                 TriggerLabels = v.TriggerLabels
            }).ToList() ?? new List<CameraViolationType>(),
            CreatedAt = DateTime.UtcNow
        };

        // Attempt to create Cloudflare Live Input asynchronously for WebRTC credentials
        if (request.IsStreaming) 
        {
            var cfResult = await _cloudflareService.CreateLiveInputAsync(request.Name);
            if (cfResult != null)
            {
                camera.CloudflareUid = cfResult.Value.uid;
                camera.WhipUrl = cfResult.Value.whipUrl;
                camera.WhepUrl = cfResult.Value.whepUrl;
                camera.IsStreaming = true; // explicitly boolean
            }
            else 
            {
                camera.IsStreaming = false;
            }
        }
        else 
        {
             var cfResult = await _cloudflareService.CreateLiveInputAsync(request.Name);
             if (cfResult != null)
             {
                 camera.CloudflareUid = cfResult.Value.uid;
                 camera.WhipUrl = cfResult.Value.whipUrl;
                 camera.WhepUrl = cfResult.Value.whepUrl;
             }
             camera.IsStreaming = false; // explicitly boolean
        }

        _context.Cameras.Add(camera);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created camera {CameraId} for tenant {TenantId}", camera.CameraId, camera.TenantId);

        var createdCamera = await _context.Cameras
            .Include(c => c.Tenant)
            .Include(c => c.ActiveViolationTypes)
            .FirstAsync(c => c.Id == camera.Id);

        TriggerVisionServiceReload();

        return CameraResponse.FromEntity(createdCamera);
    }

    public async Task<List<CameraResponse>> GetCamerasByTenantAsync(Guid tenantId)
    {
        var cameras = await _context.Cameras
            .Include(c => c.Tenant)
            .Include(c => c.ActiveViolationTypes)
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return cameras.Select(CameraResponse.FromEntity).ToList();
    }

    public async Task<CameraResponse?> GetCameraByIdAsync(Guid id)
    {
        var camera = await _context.Cameras
            .Include(c => c.Tenant)
            .Include(c => c.ActiveViolationTypes)
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
        var camera = await _context.Cameras
            .Include(c => c.ActiveViolationTypes)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (camera == null) return null;

        if (!string.IsNullOrEmpty(request.Name))
            camera.Name = request.Name;

        if (!string.IsNullOrEmpty(request.Location))
            camera.Location = request.Location;

        if (!string.IsNullOrEmpty(request.RtspUrl))
            camera.RtspUrlEncrypted = _encryptionService.Encrypt(request.RtspUrl);

        if (request.WhipUrl != null)
            camera.WhipUrl = request.WhipUrl;

        if (request.WhepUrl != null)
            camera.WhepUrl = request.WhepUrl;

        if (request.IsStreaming.HasValue)
            camera.IsStreaming = request.IsStreaming.Value;

        if (request.TargetFps.HasValue && request.TargetFps.Value > 0)
            camera.TargetFps = request.TargetFps.Value;

        if (request.ActiveViolations != null)
        {
             var requestedIds = request.ActiveViolations.Select(v => v.SopViolationTypeId).ToList();
             var approvedViolationIds = await _context.TenantViolationRequests
                 .Where(r => r.TenantId == camera.TenantId && r.Status == RequestStatus.Approved)
                 .Select(r => r.SopViolationTypeId)
                 .ToListAsync();

             var unapprovedViolations = requestedIds.Except(approvedViolationIds).ToList();
             if (unapprovedViolations.Any())
             {
                 throw new InvalidOperationException($"Cannot assign unapproved violation types: {string.Join(", ", unapprovedViolations)}");
             }

             // Handle EF Core Collection Updates by clearing the existing navigation property
             camera.ActiveViolationTypes.Clear();
             
             // Add new violations
             foreach(var v in request.ActiveViolations)
             {
                 camera.ActiveViolationTypes.Add(new CameraViolationType
                 {
                      CameraId = camera.Id,
                      SopViolationTypeId = v.SopViolationTypeId,
                      TriggerLabels = v.TriggerLabels
                 });
             }
        }

        camera.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated camera {CameraId}", id);

        var updatedCamera = await _context.Cameras
            .Include(c => c.Tenant)
            .Include(c => c.ActiveViolationTypes)
            .FirstAsync(c => c.Id == id);

        TriggerVisionServiceReload();

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
            .Include(c => c.ActiveViolationTypes)
            .FirstAsync(c => c.Id == id);

        TriggerVisionServiceReload();

        return CameraResponse.FromEntity(updatedCamera);
    }

    public async Task<bool> DeleteCameraAsync(Guid id)
    {
        var camera = await _context.Cameras.FindAsync(id);
        if (camera == null) return false;

        _context.Cameras.Remove(camera);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted camera {CameraId}", id);

        TriggerVisionServiceReload();

        return true;
    }
}
