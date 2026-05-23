using System;
using System.Collections.Generic;

namespace AlphaSurveilance.DTOs.Requests
{
    /// <summary>
    /// Bulk-marks one or more violations as false positives.
    /// Hidden from active lists, analytics, compliance & reports until "unmarked".
    /// </summary>
    public class MarkFalsePositiveRequest
    {
        public List<Guid> ViolationIds { get; set; } = new();
        public string? Reason { get; set; }
    }

    /// <summary>Bulk-restores false-positive violations back to the active list.</summary>
    public class UnmarkFalsePositiveRequest
    {
        public List<Guid> ViolationIds { get; set; } = new();
    }
}
