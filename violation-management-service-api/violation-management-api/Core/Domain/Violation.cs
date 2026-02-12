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
        public ViolationType Type { get; set; }

        public ViolationSeverity? Severity { get; set; }

        [Required]
        public string TenantId { get; set; } = string.Empty;

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
    }
}
