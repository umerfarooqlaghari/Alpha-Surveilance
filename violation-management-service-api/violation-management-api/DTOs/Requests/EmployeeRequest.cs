using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AlphaSurveilance.DTOs.Requests
{
    public class EmployeeRequest
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
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
    }
}
