using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Requests;

public class CreateTenantViolationRequestDto
{
    public Guid SopViolationTypeId { get; set; }
}

public class ResolveTenantViolationRequestDto
{
    public RequestStatus Status { get; set; } // Approved or Rejected
}
