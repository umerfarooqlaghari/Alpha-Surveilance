using System;
using System.Collections.Generic;

namespace alpha_surveilance_bff.DTOs
{
    // The "God Object" for the dashboard detail view.
    // It combines the core violation data with its full audit trail.
    public class DashboardViolationDetailDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string FramePath { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        
        public List<AuditLogDto> AuditHistory { get; set; } = new();
    }

    public class AuditLogDto
    {
        public string AuditId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
