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

    /// <summary>
    /// D-9: marks SOP types whose detections are inherently anomalous (e.g.
    /// PPE violations like "no-hardhat" or "missing-hairnet") and therefore
    /// support the non-spatial "Anomaly" rule type in the camera editor.
    /// Spatial-only types (e.g. "Unauthorized Person") leave this false and
    /// are restricted to Geofence / Dwell rules.  Replaces a fragile
    /// client-side regex on label prefixes; the regex previously misclassified
    /// label sets that didn't follow the no-/incorrect-/missing- convention.
    /// </summary>
    public bool SupportsAnomalyRule { get; set; } = false;

    public string Description { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public Sop Sop { get; set; } = null!;
    public ICollection<CameraViolationType> CameraViolations { get; set; } = new List<CameraViolationType>();
    public ICollection<TenantViolationRequest> TenantRequests { get; set; } = new List<TenantViolationRequest>();
    public ICollection<AlphaSurveilance.Core.Domain.Violation> Violations { get; set; } = new List<AlphaSurveilance.Core.Domain.Violation>();
}
