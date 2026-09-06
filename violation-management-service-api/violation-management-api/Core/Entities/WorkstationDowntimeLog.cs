using System;
using System.ComponentModel.DataAnnotations;

namespace violation_management_api.Core.Entities
{
    public class WorkstationDowntimeLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid WorkstationId { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int? DurationSeconds { get; set; }

        [MaxLength(200)]
        public string Reason { get; set; } = "Unattended / Grace Threshold Exceeded";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Workstation? Workstation { get; set; }
    }
}
