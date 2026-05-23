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

        /// <summary>
        /// Denormalized Location (sub-tenant) the violation occurred in.
        /// Copied from the Camera at write time so analytics can filter without joining Camera.
        /// Nullable for legacy rows and cameras with no Location assignment.
        /// </summary>
        public Guid? LocationId { get; set; }

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

        // ─────────────────────────────────────────────────────────────────────
        // False-positive flag
        //
        // When true, the violation is hidden from active lists, analytics,
        // compliance counts, and reports. Kept in the table for audit/forensics
        // and so it can be restored ("Unmark"). This is orthogonal to AuditStatus
        // — a violation can be Audited and then later flagged FP without losing
        // its prior audit state.
        // ─────────────────────────────────────────────────────────────────────
        public bool IsFalsePositive { get; set; } = false;
        public DateTime? FalsePositiveMarkedAt { get; set; }
        public string? FalsePositiveMarkedBy { get; set; }
        public string? FalsePositiveReason { get; set; }
    }
}
