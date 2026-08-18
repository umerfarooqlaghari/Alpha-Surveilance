using System;

namespace violation_management_api.DTOs.Responses
{
    public class AttendanceRecordResponse
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? LocationId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeExternalId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Designation { get; set; }

        public DateTime ShiftDate { get; set; }
        public DateTime FirstInTime { get; set; }
        public string? FirstInCameraName { get; set; }

        public DateTime? LastOutTime { get; set; }
        public string? LastOutCameraName { get; set; }

        public DateTime LastSeenTime { get; set; }
        public double TotalWorkMinutes { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShiftType { get; set; } = string.Empty;
    }

    public class AttendanceSummaryResponse
    {
        public string ShiftDate { get; set; } = string.Empty;
        public int TotalPresent { get; set; }
        public int TotalPresentToday { get; set; }
        public int CurrentlyOnSite { get; set; }
        public int DayShiftCount { get; set; }
        public int NightShiftCount { get; set; }
        public int TotalCompletedShifts { get; set; }
        public int CompletedShiftsCount { get; set; }
        public int InProgressShiftsCount { get; set; }
        public double AverageWorkHours { get; set; }
        public double AverageWorkMinutes { get; set; }
    }
}
