namespace violation_management_api.DTOs.Requests;

public class CameraViolationAssignment
{
    public Guid SopViolationTypeId { get; set; }
    public string? TriggerLabels { get; set; }
}

public class CreateCameraRequest
{
    public Guid TenantId { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty; // Will be encrypted
    public string WhipUrl { get; set; } = string.Empty;
    public string WhepUrl { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public double TargetFps { get; set; } = 1.0;
    public List<CameraViolationAssignment> ActiveViolations { get; set; } = new();
}

public class UpdateCameraRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? RtspUrl { get; set; } // Will be encrypted if provided
    public string? WhipUrl { get; set; }
    public string? WhepUrl { get; set; }
    public bool? IsStreaming { get; set; }
    public double? TargetFps { get; set; }
    public List<CameraViolationAssignment>? ActiveViolations { get; set; }
}

public class UpdateCameraStatusRequest
{
    public int Status { get; set; } // 0=Active, 1=Inactive, 2=Maintenance, 3=Error
}
