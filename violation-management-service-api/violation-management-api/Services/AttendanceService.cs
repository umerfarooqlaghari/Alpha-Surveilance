using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services
{
    public class AttendanceService : IAttendanceService
    {
        private readonly AppViolationDbContext _dbContext;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(AppViolationDbContext dbContext, ILogger<AttendanceService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        // Maximum shift window duration (16 hours handles day shifts, overtime, and night shifts)
        private static readonly TimeSpan MaxShiftDuration = TimeSpan.FromHours(16);
        private static DateTime ConvertToLocalTime(DateTime utcTime, string? timezoneId)
        {
            if (string.IsNullOrWhiteSpace(timezoneId))
                return utcTime;

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
            }
            catch (Exception)
            {
                return utcTime;
            }
        }

        public async Task<AttendanceRecordResponse?> ProcessAttendanceEventAsync(AttendanceEventRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EmployeeExternalId))
                return null;

            // 1. Resolve Camera with Location for Timezone mapping
            Camera? camera = null;
            if (Guid.TryParse(request.CameraId, out var cameraGuid))
            {
                camera = await _dbContext.Cameras.Include(c => c.LocationRef).FirstOrDefaultAsync(c => c.Id == cameraGuid);
            }
            if (camera == null)
            {
                camera = await _dbContext.Cameras.Include(c => c.LocationRef).FirstOrDefaultAsync(c => c.CameraId == request.CameraId);
            }

            if (camera == null || camera.AttendanceMode == AttendanceMode.None)
            {
                // Camera not configured for attendance marking
                return null;
            }

            // 2. Resolve Tenant Guidance
            Guid tenantId = camera.TenantId;
            if (Guid.TryParse(request.TenantId, out var parsedTenant))
            {
                tenantId = parsedTenant;
            }

            // 3. Resolve Employee
            var employee = await _dbContext.Employees
                .FirstOrDefaultAsync(e => e.TenantId == tenantId.ToString() && e.EmployeeId == request.EmployeeExternalId);

            if (employee == null)
            {
                // Fallback check by EmployeeId across tenants if string matching differs
                employee = await _dbContext.Employees
                    .FirstOrDefaultAsync(e => e.EmployeeId == request.EmployeeExternalId);
            }

            if (employee == null)
            {
                _logger.LogWarning("Attendance event ignored: Employee '{EmployeeExternalId}' not found.", request.EmployeeExternalId);
                return null;
            }

            var eventTime = request.Timestamp == default ? DateTime.UtcNow : request.Timestamp;
            if (eventTime.Kind != DateTimeKind.Utc)
            {
                eventTime = DateTime.SpecifyKind(eventTime, DateTimeKind.Utc);
            }
            var windowStartTime = eventTime - MaxShiftDuration;

            // Determine Shift Date & Shift Type based on the Location's Local Time
            var localEventTime = ConvertToLocalTime(eventTime, camera.LocationRef?.Timezone);
            DateTime localShiftDate = localEventTime.Date;
            DateTime shiftDate = DateTime.SpecifyKind(localShiftDate, DateTimeKind.Utc);
            string shiftType = "DayShift";
            if (localEventTime.TimeOfDay < TimeSpan.FromHours(4))
            {
                // Early morning before 04:00 AM counts as Night Shift started previous calendar day
                shiftDate = DateTime.SpecifyKind(localShiftDate.AddDays(-1), DateTimeKind.Utc);
                shiftType = "NightShift";
            }
            else if (localEventTime.TimeOfDay >= TimeSpan.FromHours(18))
            {
                shiftType = "NightShift";
            }

            // 4. Find active shift record within max shift window
            var activeRecord = await _dbContext.AttendanceRecords
                .Include(a => a.FirstInCamera)
                .Include(a => a.LastOutCamera)
                .Where(a => a.TenantId == tenantId &&
                            a.EmployeeId == employee.Id &&
                            a.FirstInTime >= windowStartTime &&
                            a.Status != AttendanceRecordStatus.Completed &&
                            a.Status != AttendanceRecordStatus.AutoClosed)
                .OrderByDescending(a => a.FirstInTime)
                .FirstOrDefaultAsync();

            AttendanceEventType eventType = AttendanceEventType.Heartbeat;
            bool isInserting = false;

            // 5. Process based on Camera AttendanceMode & FILO shift state
            if (activeRecord == null)
            {
                isInserting = true;
                // Spawning a NEW shift record
                eventType = AttendanceEventType.CheckIn;

                activeRecord = new AttendanceRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    LocationId = camera.LocationId ?? employee.LocationId,
                    EmployeeId = employee.Id,
                    EmployeeExternalId = employee.EmployeeId,
                    ShiftDate = shiftDate,
                    FirstInTime = eventTime,
                    FirstInCameraId = camera.Id,
                    LastSeenTime = eventTime,
                    Status = AttendanceRecordStatus.Active,
                    ShiftType = shiftType,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (camera.AttendanceMode == AttendanceMode.MarkOut)
                {
                    // CheckOut directly without prior CheckIn
                    activeRecord.LastOutTime = eventTime;
                    activeRecord.LastOutCameraId = camera.Id;
                    activeRecord.TotalWorkMinutes = 0.0;
                    activeRecord.Status = AttendanceRecordStatus.Present;
                    eventType = AttendanceEventType.CheckOut;
                }

                _dbContext.AttendanceRecords.Add(activeRecord);
            }
            else
            {
                // Updating EXISTING active shift record
                activeRecord.LastSeenTime = eventTime;
                activeRecord.UpdatedAt = DateTime.UtcNow;

                if (camera.AttendanceMode == AttendanceMode.MarkOut || camera.AttendanceMode == AttendanceMode.Bidirectional)
                {
                    // Continuously update LAST-OUT time on every exit/sighting
                    activeRecord.LastOutTime = eventTime;
                    activeRecord.LastOutCameraId = camera.Id;
                    activeRecord.Status = AttendanceRecordStatus.Present;

                    if (activeRecord.FirstInTime < eventTime)
                    {
                        activeRecord.TotalWorkMinutes = Math.Round((eventTime - activeRecord.FirstInTime).TotalMinutes, 2);
                    }
                    eventType = AttendanceEventType.CheckOut;
                }
                else if (camera.AttendanceMode == AttendanceMode.MarkIn)
                {
                    // Re-entry / check-in heartbeat
                    eventType = AttendanceEventType.CheckIn;
                    activeRecord.Status = AttendanceRecordStatus.Active; // Reset status back to Active since employee is back on-site
                    if (activeRecord.LastOutTime.HasValue && activeRecord.FirstInTime < activeRecord.LastOutTime.Value)
                    {
                        activeRecord.TotalWorkMinutes = Math.Round((activeRecord.LastOutTime.Value - activeRecord.FirstInTime).TotalMinutes, 2);
                    }
                }
            }

            // 6. Record Audit Event in AttendanceLog
            var attendanceLog = new AttendanceLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AttendanceRecordId = activeRecord.Id,
                EmployeeId = employee.Id,
                CameraId = camera.Id,
                EventType = eventType,
                Timestamp = eventTime,
                TrackId = request.TrackId,
                Confidence = request.Confidence,
                FrameUrl = request.FrameUrl
            };
            _dbContext.AttendanceLogs.Add(attendanceLog);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException) when (isInserting)
            {
                // Concurrency recovery: If a duplicate insert fails the unique constraint,
                // detach the failed entities, fetch the record inserted by the winning thread,
                // and update it instead.
                _dbContext.Entry(activeRecord).State = EntityState.Detached;
                _dbContext.Entry(attendanceLog).State = EntityState.Detached;

                activeRecord = await _dbContext.AttendanceRecords
                    .Include(a => a.FirstInCamera)
                    .Include(a => a.LastOutCamera)
                    .Where(a => a.TenantId == tenantId &&
                                a.EmployeeId == employee.Id &&
                                a.FirstInTime >= windowStartTime &&
                                a.Status != AttendanceRecordStatus.Completed &&
                                a.Status != AttendanceRecordStatus.AutoClosed)
                    .OrderByDescending(a => a.FirstInTime)
                    .FirstOrDefaultAsync();

                if (activeRecord != null)
                {
                    activeRecord.LastSeenTime = eventTime;
                    activeRecord.UpdatedAt = DateTime.UtcNow;

                    if (camera.AttendanceMode == AttendanceMode.MarkOut || camera.AttendanceMode == AttendanceMode.Bidirectional)
                    {
                        activeRecord.LastOutTime = eventTime;
                        activeRecord.LastOutCameraId = camera.Id;
                        activeRecord.Status = AttendanceRecordStatus.Present;

                        if (activeRecord.FirstInTime < eventTime)
                        {
                            activeRecord.TotalWorkMinutes = Math.Round((eventTime - activeRecord.FirstInTime).TotalMinutes, 2);
                        }
                        eventType = AttendanceEventType.CheckOut;
                    }
                    else if (camera.AttendanceMode == AttendanceMode.MarkIn)
                    {
                        eventType = AttendanceEventType.CheckIn;
                        activeRecord.Status = AttendanceRecordStatus.Active;
                        if (activeRecord.LastOutTime.HasValue && activeRecord.FirstInTime < activeRecord.LastOutTime.Value)
                        {
                            activeRecord.TotalWorkMinutes = Math.Round((activeRecord.LastOutTime.Value - activeRecord.FirstInTime).TotalMinutes, 2);
                        }
                    }

                    var retryLog = new AttendanceLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        AttendanceRecordId = activeRecord.Id,
                        EmployeeId = employee.Id,
                        CameraId = camera.Id,
                        EventType = eventType,
                        Timestamp = eventTime,
                        TrackId = request.TrackId,
                        Confidence = request.Confidence,
                        FrameUrl = request.FrameUrl
                    };
                    _dbContext.AttendanceLogs.Add(retryLog);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    throw;
                }
            }

            _logger.LogInformation(
                "FILO Attendance Updated: Employee '{EmployeeId}', Event='{EventType}', FirstIn='{FirstIn}', LastOut='{LastOut}'",
                employee.EmployeeId, eventType, activeRecord.FirstInTime, activeRecord.LastOutTime
            );

            return MapToResponse(activeRecord, employee, camera, camera);
        }

        public async Task<List<AttendanceRecordResponse>> GetTenantAttendanceAsync(
            Guid tenantId,
            DateTime? startDate,
            DateTime? endDate,
            Guid? locationId,
            string? employeeId,
            string? status = null)
        {
            var query = _dbContext.AttendanceRecords
                .Include(a => a.FirstInCamera)
                .Include(a => a.LastOutCamera)
                .Where(a => a.TenantId == tenantId);

            if (locationId.HasValue)
            {
                query = query.Where(a => a.LocationId == locationId.Value);
            }

            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                query = query.Where(a => a.EmployeeExternalId == employeeId);
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<AttendanceRecordStatus>(status, true, out var parsedStatus))
                {
                    query = query.Where(a => a.Status == parsedStatus);
                }
                else if (string.Equals(status, "InShift", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(a => a.Status == AttendanceRecordStatus.Active || a.Status == AttendanceRecordStatus.Present);
                }
            }

            if (startDate.HasValue)
            {
                var startUtc = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(a => a.ShiftDate >= startUtc);
            }

            if (endDate.HasValue)
            {
                var endUtc = DateTime.SpecifyKind(endDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(a => a.ShiftDate <= endUtc);
            }

            var records = await query
                .OrderByDescending(a => a.ShiftDate)
                .ThenByDescending(a => a.FirstInTime)
                .ToListAsync();

            // Resolve employee details
            var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
            var employeesMap = await _dbContext.Employees
                .Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            return records.Select(r =>
            {
                employeesMap.TryGetValue(r.EmployeeId, out var emp);
                return MapToResponse(r, emp, r.FirstInCamera, r.LastOutCamera);
            }).ToList();
        }

        public async Task<AttendanceSummaryResponse> GetAttendanceSummaryAsync(Guid tenantId, DateTime? date, Guid? locationId)
        {
            var targetDate = DateTime.SpecifyKind(date?.Date ?? DateTime.UtcNow.Date, DateTimeKind.Utc);

            var query = _dbContext.AttendanceRecords
                .Where(a => a.TenantId == tenantId && a.ShiftDate == targetDate);

            if (locationId.HasValue)
            {
                query = query.Where(a => a.LocationId == locationId.Value);
            }

            var records = await query.ToListAsync();

            int totalPresent = records.Count;
            int dayShiftCount = records.Count(r => string.Equals(r.ShiftType, "DayShift", StringComparison.OrdinalIgnoreCase));
            int nightShiftCount = records.Count(r => string.Equals(r.ShiftType, "NightShift", StringComparison.OrdinalIgnoreCase));
            int completedShifts = records.Count(r => r.Status == AttendanceRecordStatus.Completed || r.LastOutTime.HasValue);
            int inProgressShifts = records.Count(r => r.Status == AttendanceRecordStatus.Active || r.Status == AttendanceRecordStatus.Present);
            double avgMinutes = records.Where(r => r.TotalWorkMinutes > 0).Select(r => r.TotalWorkMinutes).DefaultIfEmpty(0.0).Average();
            double avgHours = Math.Round(avgMinutes / 60.0, 2);

            return new AttendanceSummaryResponse
            {
                ShiftDate = targetDate.ToString("yyyy-MM-dd"),
                TotalPresent = totalPresent,
                TotalPresentToday = totalPresent,
                CurrentlyOnSite = inProgressShifts,
                DayShiftCount = dayShiftCount,
                NightShiftCount = nightShiftCount,
                TotalCompletedShifts = completedShifts,
                CompletedShiftsCount = completedShifts,
                InProgressShiftsCount = inProgressShifts,
                AverageWorkHours = avgHours,
                AverageWorkMinutes = Math.Round(avgMinutes, 2)
            };
        }

        private static AttendanceRecordResponse MapToResponse(
            AttendanceRecord record,
            Employee? employee,
            Camera? firstInCam,
            Camera? lastOutCam)
        {
            return new AttendanceRecordResponse
            {
                Id = record.Id,
                TenantId = record.TenantId,
                LocationId = record.LocationId,
                EmployeeId = record.EmployeeId,
                EmployeeExternalId = record.EmployeeExternalId,
                EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}".Trim() : record.EmployeeExternalId,
                Department = employee?.Department,
                Designation = employee?.Designation,
                ShiftDate = record.ShiftDate,
                FirstInTime = record.FirstInTime,
                FirstInCameraName = firstInCam?.Name ?? firstInCam?.CameraId,
                LastOutTime = record.LastOutTime,
                LastOutCameraName = lastOutCam?.Name ?? lastOutCam?.CameraId,
                LastSeenTime = record.LastSeenTime,
                TotalWorkMinutes = record.TotalWorkMinutes,
                Status = record.Status == AttendanceRecordStatus.Active ? "InShift" : record.Status.ToString(),
                ShiftType = record.ShiftType
            };
        }
    }
}
