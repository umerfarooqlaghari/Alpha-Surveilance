using System;
using System.Collections.Generic;

namespace AlphaSurveilance.DTOs.Responses
{
    public class EmployeeResponse
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string? Number { get; set; }
        public string? CompanyName { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public string? Tenure { get; set; }
        public string? Grade { get; set; }
        public string? Gender { get; set; }
        public string? ManagerId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string FaceScanStatus { get; set; } = string.Empty;
        public DateTime? FaceScanCompletedAt { get; set; }
        public DateTime? FaceScanInviteSentAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
