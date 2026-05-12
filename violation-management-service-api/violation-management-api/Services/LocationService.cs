using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class LocationService : ILocationService
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<LocationService> _logger;

    public LocationService(AppViolationDbContext context, ILogger<LocationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<LocationResponse> CreateLocationAsync(CreateLocationRequest request)
    {
        if (request.TenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId is required.");

        var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId);
        if (!tenantExists)
            throw new InvalidOperationException($"Tenant with ID '{request.TenantId}' not found");

        var codeExists = await _context.Locations
            .AnyAsync(l => l.TenantId == request.TenantId && l.Code == request.Code);
        if (codeExists)
            throw new InvalidOperationException($"A location with code '{request.Code}' already exists for this tenant.");

        var location = new Location
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Name = request.Name,
            Code = request.Code,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            Timezone = request.Timezone,
            Status = LocationStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created location {LocationId} ({Code}) for tenant {TenantId}",
            location.Id, location.Code, location.TenantId);

        var created = await _context.Locations
            .Include(l => l.Tenant)
            .FirstAsync(l => l.Id == location.Id);

        return LocationResponse.FromEntity(created, cameraCount: 0);
    }

    public async Task<List<LocationResponse>> GetLocationsByTenantAsync(Guid tenantId, string? search = null)
    {
        var query = _context.Locations
            .Include(l => l.Tenant)
            .Where(l => l.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(l =>
                l.Name.ToLower().Contains(s) ||
                l.Code.ToLower().Contains(s) ||
                (l.City != null && l.City.ToLower().Contains(s)));
        }

        var locations = await query
            .OrderBy(l => l.Name)
            .Select(l => new
            {
                Location = l,
                CameraCount = _context.Cameras.Count(c => c.LocationId == l.Id)
            })
            .ToListAsync();

        return locations
            .Select(x => LocationResponse.FromEntity(x.Location, x.CameraCount))
            .ToList();
    }

    public async Task<LocationResponse?> GetLocationByIdAsync(Guid id)
    {
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location == null) return null;

        var cameraCount = await _context.Cameras.CountAsync(c => c.LocationId == id);
        return LocationResponse.FromEntity(location, cameraCount);
    }

    public async Task<LocationResponse?> UpdateLocationAsync(Guid id, UpdateLocationRequest request)
    {
        var location = await _context.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Code) && request.Code != location.Code)
        {
            var codeExists = await _context.Locations
                .AnyAsync(l => l.TenantId == location.TenantId && l.Code == request.Code && l.Id != id);
            if (codeExists)
                throw new InvalidOperationException($"A location with code '{request.Code}' already exists for this tenant.");
            location.Code = request.Code;
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) location.Name = request.Name;
        if (request.Address != null) location.Address = request.Address;
        if (request.City != null) location.City = request.City;
        if (request.Country != null) location.Country = request.Country;
        if (request.Timezone != null) location.Timezone = request.Timezone;

        if (request.Status.HasValue && Enum.IsDefined(typeof(LocationStatus), request.Status.Value))
            location.Status = (LocationStatus)request.Status.Value;

        location.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updated = await _context.Locations
            .Include(l => l.Tenant)
            .FirstAsync(l => l.Id == id);

        var cameraCount = await _context.Cameras.CountAsync(c => c.LocationId == id);
        return LocationResponse.FromEntity(updated, cameraCount);
    }

    public async Task<bool> DeleteLocationAsync(Guid id)
    {
        var location = await _context.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location == null) return false;

        // Block deletion if cameras are still attached. Caller can detach them first.
        var hasCameras = await _context.Cameras.AnyAsync(c => c.LocationId == id);
        if (hasCameras)
            throw new InvalidOperationException(
                "Location still has cameras assigned. Reassign or delete those cameras before deleting the location.");

        _context.Locations.Remove(location); // soft delete via DbContext.SaveChanges override
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted location {LocationId}", id);
        return true;
    }
}
