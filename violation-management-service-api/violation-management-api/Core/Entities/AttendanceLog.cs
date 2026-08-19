using System;
using System.ComponentModel.DataAnnotations;
using AlphaSurveilance.Core.Enums;

namespace violation_management_api.Core.Entities
{
    public class AttendanceLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid AttendanceRecordId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        public Guid CameraId { get; set; }

        public AttendanceEventType EventType { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int TrackId { get; set; }

        public double Confidence { get; set; }

        public string? FrameUrl { get; set; }

        // Navigation Properties
        public AttendanceRecord? AttendanceRecord { get; set; }
        public Camera? Camera { get; set; }
    }
}
