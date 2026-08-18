using System;

namespace violation_management_api.DTOs.Requests
{
    public class AttendanceEventRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty; // Camera Database GUID or CameraId slug
        public string EmployeeExternalId { get; set; } = string.Empty;
        public int TrackId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double Confidence { get; set; } = 1.0;
        public string? FrameUrl { get; set; }
    }
}
