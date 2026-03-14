using System;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

/// <summary>
/// Full audit trail record for resolving a violation.
/// All resolution fields are nullable to support partial / draft saves.
/// </summary>
public class ViolationAudit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Violation — one audit per violation.</summary>
    public Guid ViolationId { get; set; }
    public Violation? Violation { get; set; }

    public Guid TenantId { get; set; }

    // ── Audit Status ────────────────────────────────────────────────────────
    public AuditRecordStatus Status { get; set; } = AuditRecordStatus.Draft;

    // ── Incident Summary ────────────────────────────────────────────────────
    /// <summary>High-level executive summary of the incident.</summary>
    public string? ExecutiveSummary { get; set; }

    // ── Root Cause Analysis ─────────────────────────────────────────────────
    public string? RootCauseAnalysis { get; set; }
    public string? ContributingFactors { get; set; }

    // ── Impact Assessment ───────────────────────────────────────────────────
    /// <summary>Names / roles of affected parties. Free text or comma-separated.</summary>
    public string? StakeholdersAffected { get; set; }
    public string? EstimatedImpact { get; set; }

    // ── Response & Resolution ───────────────────────────────────────────────
    public string? MeasuresTaken { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // ── Prevention ──────────────────────────────────────────────────────────
    public string? PreventionMeasures { get; set; }
    public string? FollowUpActions { get; set; }

    // ── Sign-off ────────────────────────────────────────────────────────────
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? InternalNotes { get; set; }

    // ── Timestamps ──────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
}

public enum AuditRecordStatus
{
    Draft = 0,
    Submitted = 1,
    Reviewed = 2,
}
