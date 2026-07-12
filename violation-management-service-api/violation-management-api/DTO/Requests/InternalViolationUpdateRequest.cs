using System;

namespace AlphaSurveilance.DTO.Requests;

/// <summary>
/// [SERVICE-TO-SERVICE] Body of PATCH /api/violations/internal/{id}.
/// Sent by the Vision Inference Service to refresh the LastSeen timestamp of an
/// ongoing violation (and optionally transition its status).
/// The Python client sends: { "Timestamp": "2026-07-11T10:00:00+00:00" }.
/// Timestamp is parsed as DateTimeOffset so ISO-8601 offsets ("+00:00", "Z")
/// convert to UTC without DateTimeKind ambiguity.
/// </summary>
public class InternalViolationUpdateRequest
{
    /// <summary>New last-seen timestamp. When omitted, the server uses UtcNow.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Optional status transition (e.g. "Pending", "Audited"). Parsed
    /// case-insensitively into AuditStatus; invalid values are ignored.
    /// </summary>
    public string? Status { get; set; }
}
