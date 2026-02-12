namespace violation_management_api.DTOs.Requests;

public class CreateCameraRequest
{
    public Guid TenantId { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty; // Will be encrypted
    public bool EnableSafetyViolations { get; set; } = true;
    public bool EnableSecurityViolations { get; set; } = true;
    public bool EnableOperationalViolations { get; set; } = true;
    public bool EnableComplianceViolations { get; set; } = true;
}

public class UpdateCameraRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? RtspUrl { get; set; } // Will be encrypted if provided
    public bool? EnableSafetyViolations { get; set; }
    public bool? EnableSecurityViolations { get; set; }
    public bool? EnableOperationalViolations { get; set; }
    public bool? EnableComplianceViolations { get; set; }
}

public class UpdateCameraStatusRequest
{
    public int Status { get; set; } // 0=Active, 1=Inactive, 2=Maintenance, 3=Error
}
