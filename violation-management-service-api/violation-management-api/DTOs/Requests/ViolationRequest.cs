using System;
using AlphaSurveilance.Core.Enums;

namespace AlphaSurveilance.DTOs.Requests
{
    public class ViolationRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? CameraId { get; set; }
        public string? FramePath { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? MetadataJson { get; set; }
    }
}
