using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Enums;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services
{
    public class ReliefAnalyticsService : IReliefAnalyticsService
    {
        private readonly AppViolationDbContext _db;
        private readonly ILogger<ReliefAnalyticsService> _logger;

        public ReliefAnalyticsService(AppViolationDbContext db, ILogger<ReliefAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ReliefAnalyticsSummaryResponse> GetAnalyticsSummaryAsync(
            Guid tenantId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            Guid? locationId = null)
        {
            var effectiveStartDate = (startDate ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
            var effectiveEndDate = (endDate ?? DateTime.UtcNow.Date).Date.AddDays(1).AddTicks(-1);

            var startUtc = DateTime.SpecifyKind(effectiveStartDate, DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(effectiveEndDate, DateTimeKind.Utc);

            var wsQuery = _db.Workstations
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId && !w.IsDeleted);

            if (locationId.HasValue && locationId.Value != Guid.Empty)
            {
                wsQuery = wsQuery.Where(w => w.LocationId == locationId.Value);
            }

            var workstations = await wsQuery.ToListAsync();
            var totalWorkstations = workstations.Count;
            var activeWorkstations = workstations.Count(w => w.IsActive);
            var wsIds = workstations.Select(w => w.Id).ToList();

            var sessionQuery = _db.ReliefSessions
                .AsNoTracking()
                .Include(r => r.RelieverEmployee)
                .Include(r => r.Workstation)
                .Where(r => r.TenantId == tenantId && wsIds.Contains(r.WorkstationId) && r.PrimaryLeftAt >= startUtc && r.PrimaryLeftAt <= endUtc);

            var sessions = await sessionQuery.ToListAsync();

            var downtimeQuery = _db.WorkstationDowntimeLogs
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && wsIds.Contains(d.WorkstationId) && d.StartTime >= startUtc && d.StartTime <= endUtc);

            var downtimeLogs = await downtimeQuery.ToListAsync();

            int totalReliefEvents = sessions.Count;
            int unattendedViolations = sessions.Count(s => s.Status == ReliefSessionStatus.UnattendedViolation) +
                                       downtimeLogs.Count;

            var latencySessions = sessions.Where(s => s.HandoverLatencySeconds.HasValue && s.HandoverLatencySeconds.Value > 0).ToList();
            double avgLatency = latencySessions.Count > 0 ? latencySessions.Average(s => s.HandoverLatencySeconds!.Value) : 0.0;

            var completedReliefs = sessions.Where(s => s.ReliefDurationSeconds.HasValue && s.ReliefDurationSeconds.Value > 0).ToList();
            double avgDurationMins = completedReliefs.Count > 0 ? completedReliefs.Average(s => s.ReliefDurationSeconds!.Value) / 60.0 : 0.0;

            int totalDowntimeSecs = downtimeLogs.Sum(d => d.DurationSeconds ?? 0);
            int totalDowntimeMins = (int)Math.Ceiling(totalDowntimeSecs / 60.0);

            // Calculate overall compliance rate
            // Total monitored minutes = workstations * days * 12h shift (approx 720 mins/day)
            var totalDays = Math.Max(1, (effectiveEndDate - effectiveStartDate).Days);
            var expectedMonitoredMins = Math.Max(1, totalWorkstations * totalDays * 480); // 8-hour shift base
            double complianceRate = Math.Max(0.0, Math.Min(100.0, ((expectedMonitoredMins - totalDowntimeMins) / (double)expectedMonitoredMins) * 100.0));

            // Reliever workload breakdown
            var relieverGroups = sessions
                .Where(s => s.RelieverEmployeeId.HasValue || !string.IsNullOrWhiteSpace(s.RelieverEmployeeExternalId))
                .GroupBy(s => s.RelieverEmployeeId ?? Guid.Empty)
                .ToList();

            var topRelievers = new List<RelieverWorkloadMetric>();
            foreach (var grp in relieverGroups)
            {
                var sample = grp.First();
                var empName = sample.RelieverEmployee != null
                    ? $"{sample.RelieverEmployee.FirstName} {sample.RelieverEmployee.LastName}".Trim()
                    : sample.RelieverEmployeeExternalId ?? "Reliever";

                var reliefMins = grp.Sum(s => s.ReliefDurationSeconds ?? 0) / 60.0;
                var latencies = grp.Where(s => s.HandoverLatencySeconds.HasValue).Select(s => s.HandoverLatencySeconds!.Value).ToList();
                var avgRelieverLatency = latencies.Count > 0 ? latencies.Average() : 0.0;

                topRelievers.Add(new RelieverWorkloadMetric
                {
                    EmployeeId = sample.RelieverEmployeeId ?? Guid.Empty,
                    EmployeeName = empName,
                    EmployeeExternalId = sample.RelieverEmployeeExternalId ?? string.Empty,
                    ReliefCount = grp.Count(),
                    TotalReliefMinutes = Math.Round(reliefMins, 1),
                    AverageResponseLatencySeconds = Math.Round(avgRelieverLatency, 1)
                });
            }

            topRelievers = topRelievers.OrderByDescending(r => r.ReliefCount).Take(10).ToList();

            // Workstation metrics
            var workstationMetrics = new List<WorkstationComplianceMetric>();
            foreach (var w in workstations)
            {
                var wSessions = sessions.Where(s => s.WorkstationId == w.Id).ToList();
                var wDowntimes = downtimeLogs.Where(d => d.WorkstationId == w.Id).ToList();
                var wViolations = wSessions.Count(s => s.Status == ReliefSessionStatus.UnattendedViolation) + wDowntimes.Count;
                var wUnstaffedSecs = wDowntimes.Sum(d => d.DurationSeconds ?? 0);
                var wUnstaffedMins = Math.Round(wUnstaffedSecs / 60.0, 1);
                var wCompliance = wViolations > 0 ? Math.Max(75.0, 100.0 - (wViolations * 3.5)) : 100.0;

                workstationMetrics.Add(new WorkstationComplianceMetric
                {
                    WorkstationId = w.Id,
                    WorkstationName = w.Name,
                    WorkstationCode = w.Code,
                    StaffingCompliancePercentage = Math.Round(wCompliance, 1),
                    TotalReliefsReceived = wSessions.Count,
                    TotalViolations = wViolations,
                    TotalUnstaffedMinutes = wUnstaffedMins
                });
            }

            // Hourly distribution (24 hours)
            var hourlyDistribution = new List<HourlyReliefDistribution>();
            for (int h = 0; h < 24; h++)
            {
                var hReliefs = sessions.Count(s => s.PrimaryLeftAt.Hour == h);
                var hViolations = sessions.Count(s => s.PrimaryLeftAt.Hour == h && s.Status == ReliefSessionStatus.UnattendedViolation) +
                                  downtimeLogs.Count(d => d.StartTime.Hour == h);

                hourlyDistribution.Add(new HourlyReliefDistribution
                {
                    Hour = h,
                    ReliefEventsCount = hReliefs,
                    UnattendedIncidentsCount = hViolations
                });
            }

            return new ReliefAnalyticsSummaryResponse
            {
                TotalWorkstations = totalWorkstations,
                ActiveWorkstations = activeWorkstations,
                OverallStaffingComplianceRate = Math.Round(complianceRate, 1),
                TotalReliefEventsCount = totalReliefEvents,
                AverageHandoverLatencySeconds = Math.Round(avgLatency, 1),
                AverageReliefDurationMinutes = Math.Round(avgDurationMins, 1),
                TotalUnattendedViolationsCount = unattendedViolations,
                TotalDowntimeMinutes = totalDowntimeMins,
                TopRelievers = topRelievers,
                WorkstationMetrics = workstationMetrics,
                HourlyDistribution = hourlyDistribution
            };
        }
    }
}
