using System;
using violation_management_api.Core.Entities;

namespace AlphaSurveilance.DTOs.Responses;

public class ViolationAuditResponse
{
    public Guid Id { get; set; }
    public Guid ViolationId { get; set; }
    public Guid TenantId { get; set; }
    public AuditRecordStatus Status { get; set; }
    public string StatusLabel => Status.ToString();

    // Incident Summary
    public string? ExecutiveSummary { get; set; }

    // Root Cause Analysis
    public string? RootCauseAnalysis { get; set; }
    public string? ContributingFactors { get; set; }

    // Impact
    public string? StakeholdersAffected { get; set; }
    public string? EstimatedImpact { get; set; }

    // Response
    public string? MeasuresTaken { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Prevention
    public string? PreventionMeasures { get; set; }
    public string? FollowUpActions { get; set; }

    // Sign-off
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? InternalNotes { get; set; }

    // Audit metadata
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
}
