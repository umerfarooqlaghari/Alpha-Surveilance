using System;

namespace AlphaSurveilance.Core.Domain
{
    public class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Type { get; set; } = string.Empty; // e.g., "EmailAlert", "AuditLog"
        public string Content { get; set; } = string.Empty; // JSON Payload
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }
    }
}
