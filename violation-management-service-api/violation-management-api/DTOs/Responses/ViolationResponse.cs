using System;
using AlphaSurveilance.Core.Enums;

namespace AlphaSurveilance.DTOs.Responses
{
    public class ViolationResponse
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? CameraId { get; set; }
        public string? FramePath { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public AuditStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? MetadataJson { get; set; }

        // New human-readable fields
        public string? CameraName { get; set; }
        public string? SopName { get; set; }
        public string? ViolationTypeName { get; set; }
    }
}
