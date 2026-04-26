using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class TenantViolationRequestResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public Guid SopId { get; set; }
    public string SopName { get; set; } = string.Empty;
    public Guid SopViolationTypeId { get; set; }
    public string ViolationTypeName { get; set; } = string.Empty;
    public string? SopTriggerLabels { get; set; } // Labels pool defined on the SOP violation type
    public int Status { get; set; } // 0=Pending, 1=Approved, 2=Rejected
    public DateTime RequestedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public static TenantViolationRequestResponse FromEntity(TenantViolationRequest req)
    {
        return new TenantViolationRequestResponse
        {
            Id = req.Id,
            TenantId = req.TenantId,
            TenantName = req.Tenant?.TenantName ?? string.Empty,
            SopId = req.SopViolationType?.SopId ?? Guid.Empty,
            SopName = req.SopViolationType?.Sop?.Name ?? string.Empty,
            SopViolationTypeId = req.SopViolationTypeId,
            ViolationTypeName = req.SopViolationType?.Name ?? string.Empty,
            SopTriggerLabels = req.SopViolationType?.TriggerLabels,
            Status = (int)req.Status,
            RequestedAt = req.RequestedAt,
            ResolvedAt = req.ResolvedAt
        };
    }
}
