using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using violation_management_api.Core.Entities;
using AlphaSurveilance.Services.Interfaces;

namespace AlphaSurveilance.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ViolationAuditsController(
    AppViolationDbContext db,
    ICurrentTenantService currentTenantService) : ControllerBase
{
    private Guid GetTenantId()
    {
        var id = currentTenantService.TenantId;
        if (!id.HasValue) throw new UnauthorizedAccessException("Tenant not found in token.");
        return id.Value;
    }

    // ── GET all audits for tenant (summary list for compliance table) ─────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetTenantId();
        var audits = await db.ViolationAudits
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new ViolationAuditResponse
            {
                Id = a.Id,
                ViolationId = a.ViolationId,
                TenantId = a.TenantId,
                Status = a.Status,
                ExecutiveSummary = a.ExecutiveSummary,
                RootCauseAnalysis = a.RootCauseAnalysis,
                ContributingFactors = a.ContributingFactors,
                StakeholdersAffected = a.StakeholdersAffected,
                EstimatedImpact = a.EstimatedImpact,
                MeasuresTaken = a.MeasuresTaken,
                ResolvedBy = a.ResolvedBy,
                ResolvedAt = a.ResolvedAt,
                PreventionMeasures = a.PreventionMeasures,
                FollowUpActions = a.FollowUpActions,
                ReviewedBy = a.ReviewedBy,
                ReviewedAt = a.ReviewedAt,
                InternalNotes = a.InternalNotes,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                CreatedByUserId = a.CreatedByUserId,
            })
            .ToListAsync();

        return Ok(audits);
    }

    // ── GET audit by violationId ──────────────────────────────────────────
    [HttpGet("violation/{violationId}")]
    public async Task<IActionResult> GetByViolation(Guid violationId)
    {
        var tenantId = GetTenantId();
        var audit = await db.ViolationAudits
            .FirstOrDefaultAsync(a => a.ViolationId == violationId && a.TenantId == tenantId);

        if (audit == null) return NotFound();

        return Ok(MapToResponse(audit));
    }

    // ── POST create ───────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ViolationAuditRequest request)
    {
        var tenantId = GetTenantId();

        // Verify the violation belongs to this tenant
        var violation = await db.Violations
            .FirstOrDefaultAsync(v => v.Id == request.ViolationId && v.TenantId == tenantId);
        if (violation == null) return NotFound("Violation not found.");

        // Check no existing audit
        var exists = await db.ViolationAudits.AnyAsync(a => a.ViolationId == request.ViolationId);
        if (exists) return Conflict("An audit record already exists for this violation. Use PUT to update.");

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var audit = new ViolationAudit
        {
            ViolationId = request.ViolationId,
            TenantId = tenantId,
            Status = request.Status,
            ExecutiveSummary = request.ExecutiveSummary,
            RootCauseAnalysis = request.RootCauseAnalysis,
            ContributingFactors = request.ContributingFactors,
            StakeholdersAffected = request.StakeholdersAffected,
            EstimatedImpact = request.EstimatedImpact,
            MeasuresTaken = request.MeasuresTaken,
            ResolvedBy = request.ResolvedBy,
            ResolvedAt = ToUtc(request.ResolvedAt),
            PreventionMeasures = request.PreventionMeasures,
            FollowUpActions = request.FollowUpActions,
            ReviewedBy = request.ReviewedBy,
            ReviewedAt = ToUtc(request.ReviewedAt),
            InternalNotes = request.InternalNotes,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
        };

        // Mark violation as Audited when audit is submitted or reviewed
        if (request.Status >= AuditRecordStatus.Submitted)
        {
            violation.Status = AuditStatus.Audited;
        }

        db.ViolationAudits.Add(audit);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByViolation), new { violationId = audit.ViolationId }, MapToResponse(audit));
    }

    // ── PUT update ────────────────────────────────────────────────────────
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ViolationAuditRequest request)
    {
        var tenantId = GetTenantId();
        var audit = await db.ViolationAudits
            .Include(a => a.Violation)
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);
        if (audit == null) return NotFound();

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        audit.Status = request.Status;
        audit.ExecutiveSummary = request.ExecutiveSummary;
        audit.RootCauseAnalysis = request.RootCauseAnalysis;
        audit.ContributingFactors = request.ContributingFactors;
        audit.StakeholdersAffected = request.StakeholdersAffected;
        audit.EstimatedImpact = request.EstimatedImpact;
        audit.MeasuresTaken = request.MeasuresTaken;
        audit.ResolvedBy = request.ResolvedBy;
        audit.ResolvedAt = ToUtc(request.ResolvedAt);
        audit.PreventionMeasures = request.PreventionMeasures;
        audit.FollowUpActions = request.FollowUpActions;
        audit.ReviewedBy = request.ReviewedBy;
        audit.ReviewedAt = ToUtc(request.ReviewedAt);
        audit.InternalNotes = request.InternalNotes;
        audit.UpdatedAt = DateTime.UtcNow;
        audit.UpdatedByUserId = userId;

        // Update violation status
        if (audit.Violation != null)
        {
            audit.Violation.Status = request.Status >= AuditRecordStatus.Submitted
                ? AuditStatus.Audited
                : AuditStatus.Pending;
        }

        await db.SaveChangesAsync();
        return Ok(MapToResponse(audit));
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    /// <summary>Ensures DateTime is stored as UTC. PostgreSQL timestamptz rejects Unspecified kind.</summary>
    private static DateTime? ToUtc(DateTime? dt) =>
        dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;

    private static ViolationAuditResponse MapToResponse(ViolationAudit a) => new()
    {
        Id = a.Id,
        ViolationId = a.ViolationId,
        TenantId = a.TenantId,
        Status = a.Status,
        ExecutiveSummary = a.ExecutiveSummary,
        RootCauseAnalysis = a.RootCauseAnalysis,
        ContributingFactors = a.ContributingFactors,
        StakeholdersAffected = a.StakeholdersAffected,
        EstimatedImpact = a.EstimatedImpact,
        MeasuresTaken = a.MeasuresTaken,
        ResolvedBy = a.ResolvedBy,
        ResolvedAt = a.ResolvedAt,
        PreventionMeasures = a.PreventionMeasures,
        FollowUpActions = a.FollowUpActions,
        ReviewedBy = a.ReviewedBy,
        ReviewedAt = a.ReviewedAt,
        InternalNotes = a.InternalNotes,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        CreatedByUserId = a.CreatedByUserId,
    };
}
