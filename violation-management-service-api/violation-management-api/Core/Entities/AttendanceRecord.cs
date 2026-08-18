using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;

namespace violation_management_api.Core.Entities
{
    public class AttendanceRecord
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        public Guid? LocationId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public string EmployeeExternalId { get; set; } = string.Empty;

        [Required]
        public DateTime ShiftDate { get; set; }

        [Required]
        public DateTime FirstInTime { get; set; }

        public Guid? FirstInCameraId { get; set; }

        public DateTime? LastOutTime { get; set; }

        public Guid? LastOutCameraId { get; set; }

        public DateTime LastSeenTime { get; set; } = DateTime.UtcNow;

        public double TotalWorkMinutes { get; set; } = 0.0;

        public AttendanceRecordStatus Status { get; set; } = AttendanceRecordStatus.Active;

        public string ShiftType { get; set; } = "DayShift";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Employee? Employee { get; set; }
        public Camera? FirstInCamera { get; set; }
        public Camera? LastOutCamera { get; set; }
    }
}
