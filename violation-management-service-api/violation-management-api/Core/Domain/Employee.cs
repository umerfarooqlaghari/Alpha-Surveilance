using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AlphaSurveilance.Core.Enums;

namespace AlphaSurveilance.Core.Domain
{
    public class Employee
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
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

        // Stores Skills, Certifications, Languages, and other extra fields as JSON
        [Column(TypeName = "jsonb")]
        public string? MetadataJson { get; set; }

        public FaceScanStatus FaceScanStatus { get; set; } = FaceScanStatus.NotAssigned;
        public DateTime? FaceScanCompletedAt { get; set; }
        public DateTime? FaceScanInviteSentAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
