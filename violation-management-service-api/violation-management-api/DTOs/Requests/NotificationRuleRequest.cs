using System;
using System.Collections.Generic;

namespace AlphaSurveilance.DTOs.Requests
{
    public class TimeIntervalDto
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }

    public class NotificationRuleRequest
    {
        public string Name { get; set; } = string.Empty;
        public List<string> TargetEmails { get; set; } = new();
        public List<Guid> FilterLocationIds { get; set; } = new();
        public List<string> FilterCameraIds { get; set; } = new();
        public List<Guid> FilterViolationTypeIds { get; set; } = new();
        public List<string> FilterSeverities { get; set; } = new();
        public List<string> FilterDepartments { get; set; } = new();
        public List<TimeIntervalDto> TimeIntervals { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }
}
