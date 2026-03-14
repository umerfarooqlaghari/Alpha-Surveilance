using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class SopService : ISopService
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<SopService> _logger;

    public SopService(AppViolationDbContext context, ILogger<SopService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SopResponse> CreateSopAsync(CreateSopRequest request)
    {
        var sop = new Sop
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        _context.Sops.Add(sop);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created SOP {SopId}", sop.Id);
        return SopResponse.FromEntity(sop);
    }

    public async Task<List<SopResponse>> GetAllSopsAsync()
    {
        var sops = await _context.Sops
            .Include(s => s.ViolationTypes)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return sops.Select(SopResponse.FromEntity).ToList();
    }

    public async Task<SopResponse?> GetSopByIdAsync(Guid id)
    {
        var sop = await _context.Sops
            .Include(s => s.ViolationTypes)
            .FirstOrDefaultAsync(s => s.Id == id);

        return sop == null ? null : SopResponse.FromEntity(sop);
    }

    public async Task<SopResponse?> UpdateSopAsync(Guid id, UpdateSopRequest request)
    {
        var sop = await _context.Sops
            .Include(s => s.ViolationTypes)
            .FirstOrDefaultAsync(s => s.Id == id);
            
        if (sop == null) return null;

        if (!string.IsNullOrEmpty(request.Name))
            sop.Name = request.Name;

        if (!string.IsNullOrEmpty(request.Description))
            sop.Description = request.Description;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated SOP {SopId}", id);

        return SopResponse.FromEntity(sop);
    }

    public async Task<bool> DeleteSopAsync(Guid id)
    {
        var sop = await _context.Sops
            .Include(s => s.ViolationTypes)
            .FirstOrDefaultAsync(s => s.Id == id);
            
        if (sop == null) return false;

        // Soft delete all violation types under this SOP
        foreach (var vt in sop.ViolationTypes)
        {
            vt.IsDeleted = true;
            vt.DeletedAt = DateTime.UtcNow;
            
            // Also soft delete all tenant requests for these violation types
            var requests = await _context.TenantViolationRequests
                .Where(r => r.SopViolationTypeId == vt.Id)
                .ToListAsync();
                
            foreach (var req in requests)
            {
                req.IsDeleted = true;
                req.DeletedAt = DateTime.UtcNow;
            }
        }

        // Soft delete the SOP itself
        sop.IsDeleted = true;
        sop.DeletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted SOP {SopId} and its associated violation types and requests", id);
        return true;
    }

    public async Task<SopViolationTypeResponse> CreateSopViolationTypeAsync(Guid sopId, CreateSopViolationTypeRequest request)
    {
        var sop = await _context.Sops.FindAsync(sopId);
        if (sop == null) throw new InvalidOperationException("SOP not found");

        var violationType = new SopViolationType
        {
            Id = Guid.NewGuid(),
            SopId = sopId,
            Name = request.Name,
            ModelIdentifier = request.ModelIdentifier,
            TriggerLabels = request.TriggerLabels,
            Description = request.Description
        };

        _context.SopViolationTypes.Add(violationType);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created ViolationType {ViolationTypeId} for SOP {SopId}", violationType.Id, sopId);
        return SopViolationTypeResponse.FromEntity(violationType);
    }

    public async Task<SopViolationTypeResponse?> UpdateSopViolationTypeAsync(Guid id, UpdateSopViolationTypeRequest request)
    {
        var violationType = await _context.SopViolationTypes.FindAsync(id);
        if (violationType == null) return null;

        if (!string.IsNullOrEmpty(request.Name))
            violationType.Name = request.Name;

        if (!string.IsNullOrEmpty(request.ModelIdentifier))
            violationType.ModelIdentifier = request.ModelIdentifier;

        if (request.TriggerLabels != null)
            violationType.TriggerLabels = request.TriggerLabels;

        if (!string.IsNullOrEmpty(request.Description))
            violationType.Description = request.Description;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated ViolationType {ViolationTypeId}", id);

        return SopViolationTypeResponse.FromEntity(violationType);
    }

    public async Task<bool> DeleteSopViolationTypeAsync(Guid id)
    {
        var violationType = await _context.SopViolationTypes.FindAsync(id);
        if (violationType == null) return false;

        // Soft delete all tenant requests for this violation type
        var requests = await _context.TenantViolationRequests
            .Where(r => r.SopViolationTypeId == id)
            .ToListAsync();
            
        foreach (var req in requests)
        {
            req.IsDeleted = true;
            req.DeletedAt = DateTime.UtcNow;
        }

        violationType.IsDeleted = true;
        violationType.DeletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted ViolationType {ViolationTypeId} and its associated requests", id);
        return true;
    }
}
