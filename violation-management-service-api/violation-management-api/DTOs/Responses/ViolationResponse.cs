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
        public string? ModelIdentifier { get; set; }

        public Guid? EmployeeId { get; set; }
        public EmployeeResponse? Employee { get; set; }

        /// <summary>Pre-signed S3 URL valid for 24 h. Null when FramePath is empty or S3 is not configured.</summary>
        public string? FrameUrl { get; set; }

        // False-positive metadata — surfaced so the UI can render the FP tab and badge.
        public bool IsFalsePositive { get; set; }
        public DateTime? FalsePositiveMarkedAt { get; set; }
        public string? FalsePositiveMarkedBy { get; set; }
        public string? FalsePositiveReason { get; set; }
    }
}
