using System;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;

namespace violation_management_api.Core.Entities
{
    public class WorkstationWorkerAssignment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid WorkstationId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        public WorkstationWorkerRole Role { get; set; } = WorkstationWorkerRole.Primary;

        public TimeSpan? ShiftStartTime { get; set; }
        public TimeSpan? ShiftEndTime { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Workstation? Workstation { get; set; }
        public Employee? Employee { get; set; }
    }
}
