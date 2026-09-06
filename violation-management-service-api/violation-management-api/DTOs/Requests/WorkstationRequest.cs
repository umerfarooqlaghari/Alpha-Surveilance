using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Enums;

namespace violation_management_api.DTOs.Requests
{
    public class CreateWorkstationRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public Guid? LocationId { get; set; }

        [Required]
        public Guid CameraId { get; set; }

        /// <summary>
        /// JSON array of points, e.g. "[[0.1, 0.2], [0.5, 0.2], [0.5, 0.8], [0.1, 0.8]]"
        /// </summary>
        [Required]
        public string PolygonJson { get; set; } = "[]";

        [Range(1, 100)]
        public int RequiredPrimaryWorkers { get; set; } = 1;

        [Range(1, 50)]
        public int MaxRelievers { get; set; } = 1;

        [Range(5, 3600)]
        public int HandoverThresholdSeconds { get; set; } = 60;

        [Range(30, 86400)]
        public int MaxReliefDurationSeconds { get; set; } = 900;

        public List<Guid>? PrimaryEmployeeIds { get; set; }
        public List<Guid>? RelieverEmployeeIds { get; set; }

        public string? OperatingScheduleJson { get; set; }
    }

    public class UpdateWorkstationRequest
    {
        [MaxLength(150)]
        public string? Name { get; set; }

        [MaxLength(50)]
        public string? Code { get; set; }

        public Guid? LocationId { get; set; }

        public Guid? CameraId { get; set; }

        public string? PolygonJson { get; set; }

        public string? OperatingScheduleJson { get; set; }

        [Range(1, 100)]
        public int? RequiredPrimaryWorkers { get; set; }

        [Range(1, 50)]
        public int? MaxRelievers { get; set; }

        [Range(5, 3600)]
        public int? HandoverThresholdSeconds { get; set; }

        [Range(30, 86400)]
        public int? MaxReliefDurationSeconds { get; set; }

        public bool? IsActive { get; set; }

        public List<Guid>? PrimaryEmployeeIds { get; set; }
        public List<Guid>? RelieverEmployeeIds { get; set; }
    }

    public class AssignWorkstationWorkersRequest
    {
        [Required]
        public List<WorkerAssignmentItem> Assignments { get; set; } = new();
    }

    public class WorkerAssignmentItem
    {
        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public WorkstationWorkerRole Role { get; set; } = WorkstationWorkerRole.Primary;

        public TimeSpan? ShiftStartTime { get; set; }
        public TimeSpan? ShiftEndTime { get; set; }
    }

    /// <summary>
    /// Event payload sent by vision inference edge worker for real-time state transitions.
    /// </summary>
    public class ReliefEventIngestRequest
    {
        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid WorkstationId { get; set; }

        [Required]
        public string CameraId { get; set; } = string.Empty;

        [Required]
        public string EventType { get; set; } = string.Empty; // "PRIMARY_EXIT", "RELIEVER_ENTER", "PRIMARY_RETURN", "UNATTENDED_TIMEOUT", "OVERDUE_TIMEOUT"

        public string? EmployeeExternalId { get; set; }

        public string? Role { get; set; } // "Primary", "Reliever", "Unknown"

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int? DwellSeconds { get; set; }

        public string? FrameUrl { get; set; }

        public string? Notes { get; set; }
    }
}
