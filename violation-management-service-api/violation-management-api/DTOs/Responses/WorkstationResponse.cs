using System;
using System.Collections.Generic;
using AlphaSurveilance.Core.Enums;

namespace violation_management_api.DTOs.Responses
{
    public class WorkstationResponse
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? LocationId { get; set; }
        public string? LocationName { get; set; }
        public Guid CameraId { get; set; }
        public string CameraName { get; set; } = string.Empty;
        public string CameraExternalId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string PolygonJson { get; set; } = "[]";
        public int RequiredPrimaryWorkers { get; set; }
        public int MaxRelievers { get; set; }
        public int HandoverThresholdSeconds { get; set; }
        public int MaxReliefDurationSeconds { get; set; }
        public WorkstationStatus CurrentStatus { get; set; }
        public bool IsActive { get; set; }
        public int PrimaryWorkersCount { get; set; }
        public int RelieversCount { get; set; }
        public string? OperatingScheduleJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class WorkstationDetailResponse : WorkstationResponse
    {
        public List<WorkstationAssignmentResponse> Assignments { get; set; } = new();
        public ReliefSessionResponse? ActiveSession { get; set; }
    }

    public class WorkstationAssignmentResponse
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeExternalId { get; set; } = string.Empty;
        public WorkstationWorkerRole Role { get; set; }
        public TimeSpan? ShiftStartTime { get; set; }
        public TimeSpan? ShiftEndTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class ReliefSessionResponse
    {
        public Guid Id { get; set; }
        public Guid WorkstationId { get; set; }
        public string WorkstationName { get; set; } = string.Empty;
        public string WorkstationCode { get; set; } = string.Empty;
        public Guid CameraId { get; set; }
        public string CameraName { get; set; } = string.Empty;
        public Guid? PrimaryEmployeeId { get; set; }
        public string? PrimaryEmployeeName { get; set; }
        public string? PrimaryEmployeeExternalId { get; set; }
        public Guid? RelieverEmployeeId { get; set; }
        public string? RelieverEmployeeName { get; set; }
        public string? RelieverEmployeeExternalId { get; set; }
        public DateTime PrimaryLeftAt { get; set; }
        public DateTime? RelieverArrivedAt { get; set; }
        public int? HandoverLatencySeconds { get; set; }
        public DateTime? ReliefEndedAt { get; set; }
        public int? ReliefDurationSeconds { get; set; }
        public ReliefSessionStatus Status { get; set; }
        public Guid? ViolationId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class WorkstationLiveBoardItemResponse
    {
        public Guid WorkstationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public Guid CameraId { get; set; }
        public string CameraName { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public WorkstationStatus Status { get; set; }
        public int RequiredPrimaryWorkers { get; set; }
        public int HandoverThresholdSeconds { get; set; }
        public int MaxReliefDurationSeconds { get; set; }
        public string? ActivePrimaryWorker { get; set; }
        public string? ActiveReliever { get; set; }
        public DateTime? LastEventTimestamp { get; set; }
        public int? RemainingGraceSeconds { get; set; }
        public int? ActiveReliefDurationSeconds { get; set; }
        public int ActiveReliefsTodayCount { get; set; }
        public double TodayUptimePercentage { get; set; }
    }

    public class ReliefAnalyticsSummaryResponse
    {
        public int TotalWorkstations { get; set; }
        public int ActiveWorkstations { get; set; }
        public double OverallStaffingComplianceRate { get; set; } // e.g. 98.4%
        public int TotalReliefEventsCount { get; set; }
        public double AverageHandoverLatencySeconds { get; set; } // e.g. 42.5s
        public double AverageReliefDurationMinutes { get; set; } // e.g. 14.2 min
        public int TotalUnattendedViolationsCount { get; set; }
        public int TotalDowntimeMinutes { get; set; }

        public List<RelieverWorkloadMetric> TopRelievers { get; set; } = new();
        public List<WorkstationComplianceMetric> WorkstationMetrics { get; set; } = new();
        public List<HourlyReliefDistribution> HourlyDistribution { get; set; } = new();
    }

    public class RelieverWorkloadMetric
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeExternalId { get; set; } = string.Empty;
        public int ReliefCount { get; set; }
        public double TotalReliefMinutes { get; set; }
        public double AverageResponseLatencySeconds { get; set; }
    }

    public class WorkstationComplianceMetric
    {
        public Guid WorkstationId { get; set; }
        public string WorkstationName { get; set; } = string.Empty;
        public string WorkstationCode { get; set; } = string.Empty;
        public double StaffingCompliancePercentage { get; set; }
        public int TotalReliefsReceived { get; set; }
        public int TotalViolations { get; set; }
        public double TotalUnstaffedMinutes { get; set; }
    }

    public class HourlyReliefDistribution
    {
        public int Hour { get; set; } // 0..23
        public int ReliefEventsCount { get; set; }
        public int UnattendedIncidentsCount { get; set; }
    }
}
