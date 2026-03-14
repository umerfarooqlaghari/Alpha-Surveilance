using System;
using System.Collections.Generic;

namespace violation_management_api.Core.Entities;

public class SopViolationType
{
    public Guid Id { get; set; }
    public Guid SopId { get; set; }
    public string Name { get; set; } = string.Empty; // e.g., "RestrictedAreaAccess"
    public string ModelIdentifier { get; set; } = string.Empty; // e.g., "yolos-tiny-person"

    /// <summary>
    /// Comma-separated list of detection labels that trigger this specific violation.
    /// If empty, detections for this model are ignored for this rule (fail-resilient).
    /// </summary>
    public string TriggerLabels { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public Sop Sop { get; set; } = null!;
    public ICollection<CameraViolationType> CameraViolations { get; set; } = new List<CameraViolationType>();
    public ICollection<TenantViolationRequest> TenantRequests { get; set; } = new List<TenantViolationRequest>();
    public ICollection<AlphaSurveilance.Core.Domain.Violation> Violations { get; set; } = new List<AlphaSurveilance.Core.Domain.Violation>();
}
