using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services
{
    public interface IAttendanceService
    {
        Task<AttendanceRecordResponse?> ProcessAttendanceEventAsync(AttendanceEventRequest request);
        Task<List<AttendanceRecordResponse>> GetTenantAttendanceAsync(Guid tenantId, DateTime? startDate, DateTime? endDate, Guid? locationId, string? employeeId, string? status = null);
        Task<AttendanceSummaryResponse> GetAttendanceSummaryAsync(Guid tenantId, DateTime? date, Guid? locationId);
    }
}
