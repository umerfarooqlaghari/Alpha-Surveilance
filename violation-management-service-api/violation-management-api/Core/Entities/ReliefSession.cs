using System;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Models;

namespace violation_management_api.Core.Entities
{
    public class ReliefSession
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        public Guid? LocationId { get; set; }

        [Required]
        public Guid WorkstationId { get; set; }

        [Required]
        public Guid CameraId { get; set; }

        public Guid? PrimaryEmployeeId { get; set; }
        public string? PrimaryEmployeeExternalId { get; set; }

        public Guid? RelieverEmployeeId { get; set; }
        public string? RelieverEmployeeExternalId { get; set; }

        /// <summary>
        /// Timestamp when the primary worker stepped away from the workstation polygon.
        /// </summary>
        [Required]
        public DateTime PrimaryLeftAt { get; set; }

        /// <summary>
        /// Timestamp when the reliever entered the workstation polygon.
        /// </summary>
        public DateTime? RelieverArrivedAt { get; set; }

        /// <summary>
        /// Time taken in seconds from primary departure until reliever took over (Latency).
        /// </summary>
        public int? HandoverLatencySeconds { get; set; }

        /// <summary>
        /// Timestamp when the relief duty ended (e.g. primary worker returned or next shift).
        /// </summary>
        public DateTime? ReliefEndedAt { get; set; }

        /// <summary>
        /// Total duration in seconds the reliever was actively present on duty at the workstation.
        /// </summary>
        public int? ReliefDurationSeconds { get; set; }

        public ReliefSessionStatus Status { get; set; } = ReliefSessionStatus.PendingHandover;

        /// <summary>
        /// Linked violation ID if handover grace threshold was exceeded or unstaffed line alert triggered.
        /// </summary>
        public Guid? ViolationId { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Workstation? Workstation { get; set; }
        public Camera? Camera { get; set; }
        public Employee? PrimaryEmployee { get; set; }
        public Employee? RelieverEmployee { get; set; }
        public Violation? Violation { get; set; }
    }
}
