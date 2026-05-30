using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class EdgeDeviceService : IEdgeDeviceService
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<EdgeDeviceService> _logger;

    public EdgeDeviceService(AppViolationDbContext context, ILogger<EdgeDeviceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(EdgeDeviceResponse Device, bool IsNew)> RegisterAsync(RegisterDeviceRequest request)
    {
        if (request.TenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId is required.");
        if (string.IsNullOrWhiteSpace(request.DeviceIdentifier))
            throw new InvalidOperationException("DeviceIdentifier is required.");

        var requestedIdentifier = request.DeviceIdentifier.Trim();

        var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId);
        if (!tenantExists)
            throw new InvalidOperationException($"Tenant '{request.TenantId}' not found.");

        EdgeDevice? existing = null;
        if (Guid.TryParse(requestedIdentifier, out var requestedDeviceId))
        {
            existing = await _context.EdgeDevices
                .Include(d => d.Tenant)
                .Include(d => d.LocationRef)
                .FirstOrDefaultAsync(d =>
                    d.TenantId == request.TenantId &&
                    d.Id == requestedDeviceId &&
                    !d.IsDeleted);
        }

        existing ??= await _context.EdgeDevices
            .Include(d => d.Tenant)
            .Include(d => d.LocationRef)
            .FirstOrDefaultAsync(d =>
                d.TenantId == request.TenantId &&
                d.DeviceIdentifier == requestedIdentifier &&
                !d.IsDeleted);

        if (existing != null)
        {
            // Refresh hostname/displayname if the device reports new values.
            var dirty = false;
            if (!string.IsNullOrWhiteSpace(request.Hostname) && existing.Hostname != request.Hostname)
            {
                existing.Hostname = request.Hostname;
                dirty = true;
            }
            if (!string.IsNullOrWhiteSpace(request.DisplayName) && string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                existing.DisplayName = request.DisplayName;
                dirty = true;
            }
            existing.LastSeenAt = DateTime.UtcNow;
            if (dirty) existing.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var cameraCount = await _context.Cameras.CountAsync(c => c.DeviceId == existing.Id && !c.IsDeleted);
            _logger.LogInformation("EdgeDevice re-registered: {DeviceId} ({Identifier}) for tenant {TenantId}",
                existing.Id, existing.DeviceIdentifier, existing.TenantId);
            return (EdgeDeviceResponse.FromEntity(existing, cameraCount), false);
        }

        var device = new EdgeDevice
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            DeviceIdentifier = requestedIdentifier,
            Hostname = request.Hostname ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? $"Device {requestedIdentifier[..Math.Min(8, requestedIdentifier.Length)]}"
                : request.DisplayName,
            Status = EdgeDeviceStatus.Active,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _context.EdgeDevices.Add(device);
        await _context.SaveChangesAsync();

        var created = await _context.EdgeDevices
            .Include(d => d.Tenant)
            .FirstAsync(d => d.Id == device.Id);

        _logger.LogInformation("EdgeDevice registered: {DeviceId} ({Identifier}) for tenant {TenantId}",
            created.Id, created.DeviceIdentifier, created.TenantId);

        return (EdgeDeviceResponse.FromEntity(created, 0), true);
    }

    public async Task<bool> RecordHeartbeatAsync(Guid deviceId)
    {
        var device = await _context.EdgeDevices.FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted);
        if (device == null) return false;
        device.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<EdgeDeviceResponse> CreateAsync(CreateEdgeDeviceRequest request)
    {
        if (request.TenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId is required.");
        if (string.IsNullOrWhiteSpace(request.DeviceIdentifier))
            throw new InvalidOperationException("DeviceIdentifier is required.");

        var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId);
        if (!tenantExists)
            throw new InvalidOperationException($"Tenant '{request.TenantId}' not found.");

        if (request.LocationId.HasValue)
        {
            var locationOk = await _context.Locations
                .AnyAsync(l => l.Id == request.LocationId.Value && l.TenantId == request.TenantId);
            if (!locationOk)
                throw new InvalidOperationException("Location does not belong to the specified tenant.");
        }

        var duplicate = await _context.EdgeDevices
            .AnyAsync(d => d.TenantId == request.TenantId && d.DeviceIdentifier == request.DeviceIdentifier);
        if (duplicate)
            throw new InvalidOperationException($"A device with identifier '{request.DeviceIdentifier}' already exists for this tenant.");

        var device = new EdgeDevice
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            LocationId = request.LocationId,
            DeviceIdentifier = request.DeviceIdentifier,
            DisplayName = request.DisplayName,
            Hostname = request.Hostname ?? string.Empty,
            Status = EdgeDeviceStatus.Active,
            RegisteredAt = DateTime.UtcNow
        };

        _context.EdgeDevices.Add(device);
        await _context.SaveChangesAsync();

        var created = await LoadAsync(device.Id);
        return ToResponse(created!);
    }

    public async Task<List<EdgeDeviceResponse>> GetByTenantAsync(Guid tenantId)
    {
        var devices = await _context.EdgeDevices
            .Include(d => d.Tenant)
            .Include(d => d.LocationRef)
            .Where(d => d.TenantId == tenantId && !d.IsDeleted)
            .AsNoTracking()
            .ToListAsync();

        return await ProjectAsync(devices);
    }

    public async Task<List<EdgeDeviceResponse>> GetAllAsync()
    {
        var devices = await _context.EdgeDevices
            .Include(d => d.Tenant)
            .Include(d => d.LocationRef)
            .Where(d => !d.IsDeleted)
            .AsNoTracking()
            .ToListAsync();

        return await ProjectAsync(devices);
    }

    public async Task<EdgeDeviceResponse?> GetByIdAsync(Guid id)
    {
        var device = await LoadAsync(id);
        return device == null ? null : ToResponse(device);
    }

    public async Task<EdgeDeviceResponse?> UpdateAsync(Guid id, UpdateEdgeDeviceRequest request)
    {
        var device = await _context.EdgeDevices.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (device == null) return null;

        if (request.DisplayName != null) device.DisplayName = request.DisplayName;
        if (request.Hostname != null) device.Hostname = request.Hostname;
        if (request.LocationId.HasValue)
        {
            if (request.LocationId.Value == Guid.Empty)
            {
                device.LocationId = null;
            }
            else
            {
                var locationOk = await _context.Locations
                    .AnyAsync(l => l.Id == request.LocationId.Value && l.TenantId == device.TenantId);
                if (!locationOk)
                    throw new InvalidOperationException("Location does not belong to the device's tenant.");
                device.LocationId = request.LocationId.Value;
            }
        }
        if (request.Status.HasValue)
        {
            device.Status = (EdgeDeviceStatus)request.Status.Value;
        }
        device.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updated = await LoadAsync(id);
        return updated == null ? null : ToResponse(updated);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var device = await _context.EdgeDevices.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (device == null) return false;
        device.IsDeleted = true;
        device.DeletedAt = DateTime.UtcNow;

        // Unassign any cameras attached so they drop back into the shared pool.
        var attached = await _context.Cameras.Where(c => c.DeviceId == id).ToListAsync();
        foreach (var cam in attached) cam.DeviceId = null;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AssignCameraAsync(Guid deviceId, Guid cameraId)
    {
        var device = await _context.EdgeDevices.FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted);
        if (device == null) throw new InvalidOperationException("Device not found.");
        var camera = await _context.Cameras.FirstOrDefaultAsync(c => c.Id == cameraId && !c.IsDeleted);
        if (camera == null) throw new InvalidOperationException("Camera not found.");
        if (camera.TenantId != device.TenantId)
            throw new InvalidOperationException("Camera and device must belong to the same tenant.");

        camera.DeviceId = deviceId;
        camera.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnassignCameraAsync(Guid deviceId, Guid cameraId)
    {
        var camera = await _context.Cameras.FirstOrDefaultAsync(c =>
            c.Id == cameraId && c.DeviceId == deviceId && !c.IsDeleted);
        if (camera == null) return false;
        camera.DeviceId = null;
        camera.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    // ───────────────────────────────────────── helpers ─────────────────────

    private async Task<EdgeDevice?> LoadAsync(Guid id)
    {
        return await _context.EdgeDevices
            .Include(d => d.Tenant)
            .Include(d => d.LocationRef)
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
    }

    private EdgeDeviceResponse ToResponse(EdgeDevice device)
    {
        var attachedCameras = _context.Cameras
            .Where(c => c.DeviceId == device.Id && !c.IsDeleted)
            .Select(c => new { c.LocationId })
            .ToList();

        var distinct = attachedCameras
            .Where(c => c.LocationId.HasValue)
            .Select(c => c.LocationId!.Value)
            .Distinct()
            .ToList();

        return EdgeDeviceResponse.FromEntity(device, attachedCameras.Count, distinct);
    }

    private async Task<List<EdgeDeviceResponse>> ProjectAsync(List<EdgeDevice> devices)
    {
        if (devices.Count == 0) return new List<EdgeDeviceResponse>();
        var ids = devices.Select(d => d.Id).ToList();
        var counts = await _context.Cameras
            .Where(c => c.DeviceId.HasValue && ids.Contains(c.DeviceId!.Value) && !c.IsDeleted)
            .GroupBy(c => c.DeviceId!.Value)
            .Select(g => new { DeviceId = g.Key, Count = g.Count(), LocationIds = g.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList() })
            .ToListAsync();
        var lookup = counts.ToDictionary(c => c.DeviceId);
        return devices.Select(d =>
        {
            lookup.TryGetValue(d.Id, out var info);
            return EdgeDeviceResponse.FromEntity(d, info?.Count ?? 0, info?.LocationIds ?? new List<Guid>());
        }).ToList();
    }
}
