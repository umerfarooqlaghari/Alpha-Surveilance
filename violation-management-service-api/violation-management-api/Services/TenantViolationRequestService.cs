using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class TenantViolationRequestService : ITenantViolationRequestService
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<TenantViolationRequestService> _logger;

    public TenantViolationRequestService(
        AppViolationDbContext context,
        ILogger<TenantViolationRequestService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TenantViolationRequestResponse> CreateRequestAsync(Guid tenantId, CreateTenantViolationRequestDto request)
    {
        // Prevent duplicate requests
        var existingRequest = await _context.TenantViolationRequests
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.SopViolationTypeId == request.SopViolationTypeId);
            
        if (existingRequest != null)
        {
            throw new InvalidOperationException($"A request for this violation type already exists with status: {existingRequest.Status}");
        }

        var violationType = await _context.SopViolationTypes.FindAsync(request.SopViolationTypeId);
        if (violationType == null)
            throw new InvalidOperationException("Violation type not found.");

        var newRequest = new TenantViolationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SopViolationTypeId = request.SopViolationTypeId,
            Status = RequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _context.TenantViolationRequests.Add(newRequest);
        await _context.SaveChangesAsync();
        
        // Fetch with Includes for mapping
        var savedRequest = await _context.TenantViolationRequests
             .Include(r => r.Tenant)
             .Include(r => r.SopViolationType)
             .ThenInclude(v => v.Sop)
             .FirstAsync(r => r.Id == newRequest.Id);

        _logger.LogInformation("Tenant {TenantId} created request for ViolationType {ViolationTypeId}", tenantId, request.SopViolationTypeId);
        return TenantViolationRequestResponse.FromEntity(savedRequest);
    }

    public async Task<List<TenantViolationRequestResponse>> GetRequestsByTenantAsync(Guid tenantId)
    {
        var requests = await _context.TenantViolationRequests
            .Include(r => r.Tenant)
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        return requests.Select(TenantViolationRequestResponse.FromEntity).ToList();
    }

    public async Task<List<TenantViolationRequestResponse>> GetApprovedRequestsByTenantAsync(Guid tenantId)
    {
        var requests = await _context.TenantViolationRequests
            .Include(r => r.Tenant)
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .Where(r => r.TenantId == tenantId && r.Status == RequestStatus.Approved)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        return requests.Select(TenantViolationRequestResponse.FromEntity).ToList();
    }

    public async Task<List<TenantViolationRequestResponse>> GetAllPendingRequestsAsync()
    {
        var requests = await _context.TenantViolationRequests
            .Include(r => r.Tenant)
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .Where(r => r.Status == RequestStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync();

        return requests.Select(TenantViolationRequestResponse.FromEntity).ToList();
    }

    public async Task<TenantViolationRequestResponse?> ResolveRequestAsync(Guid requestId, RequestStatus status)
    {
        if (status == RequestStatus.Pending)
            throw new ArgumentException("Cannot resolve a request to 'Pending' status.");

        var request = await _context.TenantViolationRequests
            .Include(r => r.Tenant)
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .FirstOrDefaultAsync(r => r.Id == requestId);
            
        if (request == null) return null;

        request.Status = status;
        request.ResolvedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Request {RequestId} resolved to {Status}", requestId, status);

        return TenantViolationRequestResponse.FromEntity(request);
    }

    public async Task<TenantViolationRequestResponse> AssignProactiveRequestAsync(Guid tenantId, Guid violationTypeId)
    {
        // Check if there is already a request for this
        var existingRequest = await _context.TenantViolationRequests
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.SopViolationTypeId == violationTypeId);

        if (existingRequest != null)
        {
            if (existingRequest.Status == RequestStatus.Approved)
            {
                return TenantViolationRequestResponse.FromEntity(existingRequest); // Already approved
            }

            // Auto-approve pending or rejected
            existingRequest.Status = RequestStatus.Approved;
            existingRequest.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Proactively approved existing request {RequestId} for Tenant {TenantId}", existingRequest.Id, tenantId);
            return TenantViolationRequestResponse.FromEntity(existingRequest);
        }

        var violationType = await _context.SopViolationTypes.FindAsync(violationTypeId);
        if (violationType == null)
            throw new InvalidOperationException("Violation type not found.");

        var newRequest = new TenantViolationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SopViolationTypeId = violationTypeId,
            Status = RequestStatus.Approved, // Auto-approved
            RequestedAt = DateTime.UtcNow,
            ResolvedAt = DateTime.UtcNow
        };

        _context.TenantViolationRequests.Add(newRequest);
        await _context.SaveChangesAsync();

        var savedRequest = await _context.TenantViolationRequests
             .Include(r => r.Tenant)
             .Include(r => r.SopViolationType)
             .ThenInclude(v => v.Sop)
             .FirstAsync(r => r.Id == newRequest.Id);

        _logger.LogInformation("Proactively assigned and approved ViolationType {ViolationTypeId} for Tenant {TenantId}", violationTypeId, tenantId);
        return TenantViolationRequestResponse.FromEntity(savedRequest);
    }

    public async Task<List<TenantViolationRequestResponse>> GetAllRequestsAsync()
    {
        var requests = await _context.TenantViolationRequests
            .Include(r => r.Tenant)
            .Include(r => r.SopViolationType)
            .ThenInclude(v => v.Sop)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        return requests.Select(TenantViolationRequestResponse.FromEntity).ToList();
    }

    public async Task<bool> UnassignAsync(Guid requestId)
    {
        var request = await _context.TenantViolationRequests.FindAsync(requestId);
        if (request == null) return false;

        _context.TenantViolationRequests.Remove(request); // This triggers soft delete via DB Context override
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Unassigned/Soft-deleted association {RequestId}", requestId);
        return true;
    }
}
