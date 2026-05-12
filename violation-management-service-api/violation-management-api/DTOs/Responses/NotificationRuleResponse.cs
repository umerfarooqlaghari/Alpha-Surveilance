using System;
using System.Collections.Generic;
using AlphaSurveilance.DTOs.Requests;

namespace AlphaSurveilance.DTOs.Responses
{
    public class NotificationRuleResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> TargetEmails { get; set; } = new();
        public List<Guid> FilterLocationIds { get; set; } = new();
        public List<string> FilterCameraIds { get; set; } = new();
        public List<Guid> FilterViolationTypeIds { get; set; } = new();
        public List<string> FilterSeverities { get; set; } = new();
        public List<string> FilterDepartments { get; set; } = new();
        public List<TimeIntervalDto> TimeIntervals { get; set; } = new();
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
