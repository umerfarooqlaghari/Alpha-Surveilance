using System;

namespace violation_management_api.Core.Entities;

public class CameraViolationType
{
    public Guid CameraId { get; set; }
    public Guid SopViolationTypeId { get; set; }
    public string? TriggerLabels { get; set; }

    // Navigation properties
    public Camera Camera { get; set; } = null!;
    public SopViolationType SopViolationType { get; set; } = null!;
}
