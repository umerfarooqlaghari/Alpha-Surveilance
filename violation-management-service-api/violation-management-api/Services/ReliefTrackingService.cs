using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services
{
    public class ReliefTrackingService : IReliefTrackingService
    {
        private readonly AppViolationDbContext _db;
        private readonly ILogger<ReliefTrackingService> _logger;

        public ReliefTrackingService(AppViolationDbContext db, ILogger<ReliefTrackingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ReliefSessionResponse?> ProcessReliefEventAsync(ReliefEventIngestRequest request)
        {
            if (request == null) return null;

            var workstation = await _db.Workstations
                .Include(w => w.Camera)
                .FirstOrDefaultAsync(w => w.TenantId == request.TenantId && w.Id == request.WorkstationId && !w.IsDeleted);

            if (workstation == null)
            {
                _logger.LogWarning("Relief event received for unknown workstation {WorkstationId}", request.WorkstationId);
                return null;
            }

            // Resolve employee if external ID provided
            Employee? employee = null;
            if (!string.IsNullOrWhiteSpace(request.EmployeeExternalId))
            {
                employee = await _db.Employees
                    .FirstOrDefaultAsync(e => e.TenantId == request.TenantId.ToString() && e.EmployeeId == request.EmployeeExternalId);
            }

            var activeSession = await _db.ReliefSessions
                .Include(r => r.PrimaryEmployee)
                .Include(r => r.RelieverEmployee)
                .Include(r => r.Camera)
                .Where(r => r.TenantId == request.TenantId && r.WorkstationId == request.WorkstationId &&
                           (r.Status == ReliefSessionStatus.PendingHandover || r.Status == ReliefSessionStatus.ActiveRelief))
                .OrderByDescending(r => r.PrimaryLeftAt)
                .FirstOrDefaultAsync();

            var eventType = (request.EventType ?? string.Empty).Trim().ToUpperInvariant();
            ReliefSession targetSession;

            switch (eventType)
            {
                case "PRIMARY_EXIT":
                    if (activeSession == null)
                    {
                        targetSession = new ReliefSession
                        {
                            TenantId = request.TenantId,
                            LocationId = workstation.LocationId,
                            WorkstationId = workstation.Id,
                            CameraId = workstation.CameraId,
                            PrimaryEmployeeId = employee?.Id,
                            PrimaryEmployeeExternalId = request.EmployeeExternalId,
                            PrimaryLeftAt = request.Timestamp,
                            Status = ReliefSessionStatus.PendingHandover,
                            Notes = request.Notes,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.ReliefSessions.Add(targetSession);
                    }
                    else
                    {
                        targetSession = activeSession;
                    }
                    workstation.CurrentStatus = WorkstationStatus.PendingHandover;
                    break;

                case "RELIEVER_ENTER":
                    if (activeSession == null)
                    {
                        // Direct relief takeover
                        targetSession = new ReliefSession
                        {
                            TenantId = request.TenantId,
                            LocationId = workstation.LocationId,
                            WorkstationId = workstation.Id,
                            CameraId = workstation.CameraId,
                            PrimaryLeftAt = request.Timestamp,
                            RelieverArrivedAt = request.Timestamp,
                            RelieverEmployeeId = employee?.Id,
                            RelieverEmployeeExternalId = request.EmployeeExternalId,
                            HandoverLatencySeconds = 0,
                            Status = ReliefSessionStatus.ActiveRelief,
                            Notes = request.Notes,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.ReliefSessions.Add(targetSession);
                    }
                    else
                    {
                        targetSession = activeSession;
                        targetSession.RelieverArrivedAt = request.Timestamp;
                        targetSession.RelieverEmployeeId = employee?.Id ?? targetSession.RelieverEmployeeId;
                        targetSession.RelieverEmployeeExternalId = request.EmployeeExternalId ?? targetSession.RelieverEmployeeExternalId;
                        targetSession.HandoverLatencySeconds = Math.Max(0, (int)(request.Timestamp - targetSession.PrimaryLeftAt).TotalSeconds);
                        targetSession.Status = ReliefSessionStatus.ActiveRelief;
                        targetSession.UpdatedAt = DateTime.UtcNow;
                    }
                    workstation.CurrentStatus = WorkstationStatus.UnderRelief;
                    break;

                case "PRIMARY_RETURN":
                    if (activeSession != null)
                    {
                        targetSession = activeSession;
                        targetSession.ReliefEndedAt = request.Timestamp;
                        if (targetSession.RelieverArrivedAt.HasValue)
                        {
                            targetSession.ReliefDurationSeconds = Math.Max(0, (int)(request.Timestamp - targetSession.RelieverArrivedAt.Value).TotalSeconds);
                        }
                        targetSession.Status = ReliefSessionStatus.Completed;
                        targetSession.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        targetSession = new ReliefSession
                        {
                            TenantId = request.TenantId,
                            LocationId = workstation.LocationId,
                            WorkstationId = workstation.Id,
                            CameraId = workstation.CameraId,
                            PrimaryEmployeeId = employee?.Id,
                            PrimaryEmployeeExternalId = request.EmployeeExternalId,
                            PrimaryLeftAt = request.Timestamp,
                            ReliefEndedAt = request.Timestamp,
                            Status = ReliefSessionStatus.Completed,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.ReliefSessions.Add(targetSession);
                    }
                    workstation.CurrentStatus = WorkstationStatus.Staffed;
                    break;

                case "UNATTENDED_TIMEOUT":
                    if (activeSession != null)
                    {
                        targetSession = activeSession;
                        targetSession.Status = ReliefSessionStatus.UnattendedViolation;
                        targetSession.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        targetSession = new ReliefSession
                        {
                            TenantId = request.TenantId,
                            LocationId = workstation.LocationId,
                            WorkstationId = workstation.Id,
                            CameraId = workstation.CameraId,
                            PrimaryLeftAt = request.Timestamp,
                            Status = ReliefSessionStatus.UnattendedViolation,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.ReliefSessions.Add(targetSession);
                    }
                    workstation.CurrentStatus = WorkstationStatus.UnattendedViolation;

                    // Log Downtime
                    _db.WorkstationDowntimeLogs.Add(new WorkstationDowntimeLog
                    {
                        TenantId = request.TenantId,
                        WorkstationId = workstation.Id,
                        StartTime = targetSession.PrimaryLeftAt,
                        EndTime = request.Timestamp,
                        DurationSeconds = Math.Max(0, (int)(request.Timestamp - targetSession.PrimaryLeftAt).TotalSeconds),
                        Reason = "Handover Grace Threshold Expired",
                        CreatedAt = DateTime.UtcNow
                    });
                    break;

                case "OVERDUE_TIMEOUT":
                    if (activeSession != null)
                    {
                        targetSession = activeSession;
                        targetSession.Status = ReliefSessionStatus.OverdueAlert;
                        targetSession.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        targetSession = new ReliefSession
                        {
                            TenantId = request.TenantId,
                            LocationId = workstation.LocationId,
                            WorkstationId = workstation.Id,
                            CameraId = workstation.CameraId,
                            PrimaryLeftAt = request.Timestamp,
                            Status = ReliefSessionStatus.OverdueAlert,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.ReliefSessions.Add(targetSession);
                    }
                    break;

                default:
                    _logger.LogInformation("Unhandled relief event type {EventType}", eventType);
                    return null;
            }

            workstation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Processed relief event {EventType} for workstation {WorkstationCode}, new status {Status}",
                eventType, workstation.Code, workstation.CurrentStatus);

            return new ReliefSessionResponse
            {
                Id = targetSession.Id,
                WorkstationId = targetSession.WorkstationId,
                WorkstationName = workstation.Name,
                WorkstationCode = workstation.Code,
                CameraId = targetSession.CameraId,
                CameraName = workstation.Camera?.Name ?? "Camera",
                PrimaryEmployeeId = targetSession.PrimaryEmployeeId,
                PrimaryEmployeeName = targetSession.PrimaryEmployee != null ? $"{targetSession.PrimaryEmployee.FirstName} {targetSession.PrimaryEmployee.LastName}".Trim() : targetSession.PrimaryEmployeeExternalId,
                PrimaryEmployeeExternalId = targetSession.PrimaryEmployeeExternalId,
                RelieverEmployeeId = targetSession.RelieverEmployeeId,
                RelieverEmployeeName = targetSession.RelieverEmployee != null ? $"{targetSession.RelieverEmployee.FirstName} {targetSession.RelieverEmployee.LastName}".Trim() : targetSession.RelieverEmployeeExternalId,
                RelieverEmployeeExternalId = targetSession.RelieverEmployeeExternalId,
                PrimaryLeftAt = targetSession.PrimaryLeftAt,
                RelieverArrivedAt = targetSession.RelieverArrivedAt,
                HandoverLatencySeconds = targetSession.HandoverLatencySeconds,
                ReliefEndedAt = targetSession.ReliefEndedAt,
                ReliefDurationSeconds = targetSession.ReliefDurationSeconds,
                Status = targetSession.Status,
                ViolationId = targetSession.ViolationId,
                Notes = targetSession.Notes,
                CreatedAt = targetSession.CreatedAt
            };
        }

        public async Task<List<ReliefSessionResponse>> GetSessionsAsync(
            Guid tenantId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            Guid? workstationId = null,
            string? status = null,
            string? employeeExternalId = null)
        {
            var query = _db.ReliefSessions
                .AsNoTracking()
                .Include(r => r.Workstation)
                .Include(r => r.Camera)
                .Include(r => r.PrimaryEmployee)
                .Include(r => r.RelieverEmployee)
                .Where(r => r.TenantId == tenantId);

            if (startDate.HasValue)
            {
                var startUtc = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(r => r.PrimaryLeftAt >= startUtc);
            }

            if (endDate.HasValue)
            {
                var endUtc = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                query = query.Where(r => r.PrimaryLeftAt <= endUtc);
            }

            if (workstationId.HasValue && workstationId.Value != Guid.Empty)
            {
                query = query.Where(r => r.WorkstationId == workstationId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status) && status.ToUpperInvariant() != "ALL")
            {
                if (Enum.TryParse<ReliefSessionStatus>(status, true, out var parsedStatus))
                {
                    query = query.Where(r => r.Status == parsedStatus);
                }
            }

            if (!string.IsNullOrWhiteSpace(employeeExternalId))
            {
                var empTrim = employeeExternalId.Trim();
                query = query.Where(r => r.PrimaryEmployeeExternalId == empTrim || r.RelieverEmployeeExternalId == empTrim);
            }

            var sessions = await query
                .OrderByDescending(r => r.PrimaryLeftAt)
                .Take(500)
                .ToListAsync();

            return sessions.Select(r => new ReliefSessionResponse
            {
                Id = r.Id,
                WorkstationId = r.WorkstationId,
                WorkstationName = r.Workstation?.Name ?? "Unknown Workstation",
                WorkstationCode = r.Workstation?.Code ?? string.Empty,
                CameraId = r.CameraId,
                CameraName = r.Camera?.Name ?? "Camera",
                PrimaryEmployeeId = r.PrimaryEmployeeId,
                PrimaryEmployeeName = r.PrimaryEmployee != null ? $"{r.PrimaryEmployee.FirstName} {r.PrimaryEmployee.LastName}".Trim() : r.PrimaryEmployeeExternalId,
                PrimaryEmployeeExternalId = r.PrimaryEmployeeExternalId,
                RelieverEmployeeId = r.RelieverEmployeeId,
                RelieverEmployeeName = r.RelieverEmployee != null ? $"{r.RelieverEmployee.FirstName} {r.RelieverEmployee.LastName}".Trim() : r.RelieverEmployeeExternalId,
                RelieverEmployeeExternalId = r.RelieverEmployeeExternalId,
                PrimaryLeftAt = r.PrimaryLeftAt,
                RelieverArrivedAt = r.RelieverArrivedAt,
                HandoverLatencySeconds = r.HandoverLatencySeconds,
                ReliefEndedAt = r.ReliefEndedAt,
                ReliefDurationSeconds = r.ReliefDurationSeconds,
                Status = r.Status,
                ViolationId = r.ViolationId,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt
            }).ToList();
        }

        public async Task<List<WorkstationLiveBoardItemResponse>> GetLiveBoardAsync(Guid tenantId, Guid? locationId = null)
        {
            var query = _db.Workstations
                .AsNoTracking()
                .Include(w => w.LocationRef)
                .Include(w => w.Camera)
                .Include(w => w.WorkerAssignments)
                    .ThenInclude(a => a.Employee)
                .Where(w => w.TenantId == tenantId && !w.IsDeleted);

            if (locationId.HasValue && locationId.Value != Guid.Empty)
            {
                query = query.Where(w => w.LocationId == locationId.Value);
            }

            var workstations = await query.OrderBy(w => w.Code).ToListAsync();
            var todayUtc = DateTime.UtcNow.Date;

            var activeSessions = await _db.ReliefSessions
                .AsNoTracking()
                .Include(r => r.PrimaryEmployee)
                .Include(r => r.RelieverEmployee)
                .Where(r => r.TenantId == tenantId && (r.Status == ReliefSessionStatus.PendingHandover || r.Status == ReliefSessionStatus.ActiveRelief))
                .ToListAsync();

            var todaySessions = await _db.ReliefSessions
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.PrimaryLeftAt >= todayUtc)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var board = new List<WorkstationLiveBoardItemResponse>();

            foreach (var w in workstations)
            {
                var active = activeSessions.FirstOrDefault(s => s.WorkstationId == w.Id);
                var todayWorkstationSessions = todaySessions.Where(s => s.WorkstationId == w.Id).ToList();

                int? remainingGrace = null;
                int? activeDuration = null;

                if (active != null)
                {
                    if (active.Status == ReliefSessionStatus.PendingHandover)
                    {
                        var elapsed = (int)(now - active.PrimaryLeftAt).TotalSeconds;
                        remainingGrace = Math.Max(0, w.HandoverThresholdSeconds - elapsed);
                    }
                    else if (active.Status == ReliefSessionStatus.ActiveRelief && active.RelieverArrivedAt.HasValue)
                    {
                        activeDuration = Math.Max(0, (int)(now - active.RelieverArrivedAt.Value).TotalSeconds);
                    }
                }

                var primaryWorker = w.WorkerAssignments.FirstOrDefault(a => a.IsActive && a.Role == WorkstationWorkerRole.Primary)?.Employee;
                var relieverWorker = w.WorkerAssignments.FirstOrDefault(a => a.IsActive && a.Role == WorkstationWorkerRole.Reliever)?.Employee;

                var totalViolations = todayWorkstationSessions.Count(s => s.Status == ReliefSessionStatus.UnattendedViolation);
                double uptime = totalViolations > 0 ? Math.Max(70.0, 100.0 - (totalViolations * 5.0)) : 100.0;

                board.Add(new WorkstationLiveBoardItemResponse
                {
                    WorkstationId = w.Id,
                    Name = w.Name,
                    Code = w.Code,
                    CameraId = w.CameraId,
                    CameraName = w.Camera?.Name ?? "Camera",
                    LocationName = w.LocationRef?.Name,
                    Status = w.CurrentStatus,
                    RequiredPrimaryWorkers = w.RequiredPrimaryWorkers,
                    HandoverThresholdSeconds = w.HandoverThresholdSeconds,
                    MaxReliefDurationSeconds = w.MaxReliefDurationSeconds,
                    ActivePrimaryWorker = active?.PrimaryEmployee != null ? $"{active.PrimaryEmployee.FirstName} {active.PrimaryEmployee.LastName}".Trim() : primaryWorker != null ? $"{primaryWorker.FirstName} {primaryWorker.LastName}".Trim() : "Assigned Primary",
                    ActiveReliever = active?.RelieverEmployee != null ? $"{active.RelieverEmployee.FirstName} {active.RelieverEmployee.LastName}".Trim() : active?.Status == ReliefSessionStatus.ActiveRelief ? "Active Reliever" : null,
                    LastEventTimestamp = active?.UpdatedAt ?? w.UpdatedAt,
                    RemainingGraceSeconds = remainingGrace,
                    ActiveReliefDurationSeconds = activeDuration,
                    ActiveReliefsTodayCount = todayWorkstationSessions.Count,
                    TodayUptimePercentage = uptime
                });
            }

            return board;
        }
    }
}
