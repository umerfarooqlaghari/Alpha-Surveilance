using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AlphaSurveilance.Core.Enums;
using violation_management_api.Core.Entities;

namespace violation_management_api.Core.Entities
{
    public class Workstation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        public Guid? LocationId { get; set; }

        [Required]
        public Guid CameraId { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// JSON-serialized polygon coordinates: [[x1, y1], [x2, y2], ...] (normalized 0..1 or pixel space).
        /// </summary>
        [Required]
        [Column(TypeName = "jsonb")]
        public string PolygonJson { get; set; } = "[]";

        /// <summary>
        /// Expected minimum number of primary workers required at this station.
        /// </summary>
        public int RequiredPrimaryWorkers { get; set; } = 1;

        /// <summary>
        /// Maximum number of relievers allowed simultaneously at this station.
        /// </summary>
        public int MaxRelievers { get; set; } = 1;

        /// <summary>
        /// Handover grace threshold in seconds before an unstaffed alert/violation triggers (e.g. 30s to 300s).
        /// </summary>
        public int HandoverThresholdSeconds { get; set; } = 60;

        /// <summary>
        /// Maximum allowed duration in seconds for a reliever to cover before supervisor alert (e.g. 900s = 15m).
        /// </summary>
        public int MaxReliefDurationSeconds { get; set; } = 900;

        /// <summary>
        /// Optional JSON-serialized operating timing windows / shifts:
        /// [{"id":"...","label":"Morning Shift","startTime":"08:00","endTime":"16:00","daysOfWeek":[1,2,3,4,5],"isActive":true}]
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? OperatingScheduleJson { get; set; }

        public WorkstationStatus CurrentStatus { get; set; } = WorkstationStatus.Staffed;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        // Navigation
        public Tenant? Tenant { get; set; }
        public Location? LocationRef { get; set; }
        public Camera? Camera { get; set; }
        public ICollection<WorkstationWorkerAssignment> WorkerAssignments { get; set; } = new List<WorkstationWorkerAssignment>();
        public ICollection<ReliefSession> ReliefSessions { get; set; } = new List<ReliefSession>();
        public ICollection<WorkstationDowntimeLog> DowntimeLogs { get; set; } = new List<WorkstationDowntimeLog>();
    }
}
