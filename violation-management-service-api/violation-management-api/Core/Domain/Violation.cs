using System;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Enums;

namespace AlphaSurveilance.Core.Domain
{
    public class Violation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();



        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        public string? CameraId { get; set; }
        
        public string? FramePath { get; set; } = string.Empty;
    
        [Required]
        public string CorrelationId { get; set; } = string.Empty;

        public AuditStatus Status { get; set; } = AuditStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public string? MetadataJson { get; set; }

        // Link to the new SOP hierarchy
        public Guid? SopViolationTypeId { get; set; }
        public violation_management_api.Core.Entities.SopViolationType? SopViolationType { get; set; }

        // Link to the employee (if identified via facial recognition)
        public Guid? EmployeeId { get; set; }
        public Employee? Employee { get; set; }
    }
}
