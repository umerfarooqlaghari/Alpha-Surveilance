using System;
using System.Collections.Generic;

namespace violation_management_api.Core.Entities;

public class NotificationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>
    /// Friendly name for the rule (e.g., "Night Shift Warehouse Alerts")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON array of email addresses to notify
    /// </summary>
    public string TargetEmailsJson { get; set; } = "[]";

    // --- Filters (Stored as JSON Arrays) ---
    
    public string FilterLocationIdsJson { get; set; } = "[]";
    public string FilterCameraIdsJson { get; set; } = "[]";
    public string FilterViolationTypeIdsJson { get; set; } = "[]";
    public string FilterSeveritiesJson { get; set; } = "[]";
    public string FilterDepartmentsJson { get; set; } = "[]";

    // --- Timings ---

    /// <summary>
    /// Serialized JSON array of { TimeOfDayStart: TimeSpan, TimeOfDayEnd: TimeSpan }
    /// </summary>
    public string TimeIntervalsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant? Tenant { get; set; }
}
