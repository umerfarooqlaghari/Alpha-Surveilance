using System;
using System.Collections.Generic;

namespace violation_management_api.Core.Entities;

public class Sop
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // e.g. "HumanDetection" or "Restaurant SOPs"
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation property
    public ICollection<SopViolationType> ViolationTypes { get; set; } = new List<SopViolationType>();
}
