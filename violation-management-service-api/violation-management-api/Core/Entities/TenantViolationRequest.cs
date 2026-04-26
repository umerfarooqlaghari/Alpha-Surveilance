using System;
using AlphaSurveilance.Core.Domain;

namespace violation_management_api.Core.Entities;

public class TenantViolationRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SopViolationTypeId { get; set; }
    
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public SopViolationType SopViolationType { get; set; } = null!;
}

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
