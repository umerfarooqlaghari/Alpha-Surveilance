using System.Text.Json;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;

namespace AlphaSurveilance.Extensions
{
    public static class EmployeeExtensions
    {
        public static EmployeeResponse ToResponse(this Employee employee)
        {
            var metadata = string.IsNullOrEmpty(employee.MetadataJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(employee.MetadataJson) ?? new Dictionary<string, object>();

            return new EmployeeResponse
            {
                Id = employee.Id,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Email = employee.Email,
                EmployeeId = employee.EmployeeId,
                Number = employee.Number,
                CompanyName = employee.CompanyName,
                Designation = employee.Designation,
                Department = employee.Department,
                Tenure = employee.Tenure,
                Grade = employee.Grade,
                Gender = employee.Gender,
                ManagerId = employee.ManagerId,
                Metadata = metadata,
                FaceScanStatus = employee.FaceScanStatus.ToString(),
                FaceScanCompletedAt = employee.FaceScanCompletedAt,
                FaceScanInviteSentAt = employee.FaceScanInviteSentAt,
                CreatedAt = employee.CreatedAt,
                UpdatedAt = employee.UpdatedAt
            };
        }

        public static void UpdateFromRequest(this Employee employee, EmployeeRequest request)
        {
            employee.FirstName = request.FirstName;
            employee.LastName = request.LastName;
            employee.Email = request.Email;
            employee.EmployeeId = request.EmployeeId;
            employee.Number = request.Number;
            employee.CompanyName = request.CompanyName;
            employee.Designation = request.Designation;
            employee.Department = request.Department;
            employee.Tenure = request.Tenure;
            employee.Grade = request.Grade;
            employee.Gender = request.Gender;
            employee.ManagerId = request.ManagerId;
            
            if (request.Metadata != null)
            {
                employee.MetadataJson = JsonSerializer.Serialize(request.Metadata);
            }
            
            employee.UpdatedAt = DateTime.UtcNow;
        }
    }
}
