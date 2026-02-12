using System;
using System.ComponentModel.DataAnnotations;

namespace audit_api.Core.Domain
{
    // The AuditLog is our primary "Time-Series" model.
    // In TimescaleDB, we will turn this table into a 'Hypertable' partitioned by Timestamp.
    public class AuditLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ViolationId { get; set; }

        [Required]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public string ViolationType { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
