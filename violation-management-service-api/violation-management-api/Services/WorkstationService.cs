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

using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace violation_management_api.Services
{
    public class WorkstationService : IWorkstationService
    {
        private readonly AppViolationDbContext _db;
        private readonly ILogger<WorkstationService> _logger;
        private readonly IConfiguration? _configuration;
        private readonly IHttpClientFactory? _httpClientFactory;

        public WorkstationService(
            AppViolationDbContext db,
            ILogger<WorkstationService> logger,
            IConfiguration? configuration = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            _db = db;
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        private void TriggerVisionServiceReload()
        {
            if (_configuration == null) return;
            var baseUrl = _configuration.GetValue<string>("VisionService:BaseUrl");
            if (string.IsNullOrEmpty(baseUrl)) return;

            var internalApiKey = _configuration["InternalApi:ApiKey"];

            _ = Task.Run(async () =>
            {
                var url = $"{baseUrl.TrimEnd('/')}/streams/reload";
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    if (!string.IsNullOrWhiteSpace(internalApiKey))
                    {
                        request.Headers.Add("X-Internal-Api-Key", internalApiKey);
                    }
                    var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var response = await client.SendAsync(request);
                    _logger.LogInformation("Triggered vision service reload on {Url}, status: {Status}", url, response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to trigger vision service reload on {Url}: {Error}", url, ex.Message);
                }
            });
        }

        public async Task<List<WorkstationResponse>> GetWorkstationsAsync(Guid tenantId, Guid? locationId = null, Guid? cameraId = null)
        {
            var query = _db.Workstations
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId && !w.IsDeleted);

            if (locationId.HasValue && locationId.Value != Guid.Empty)
            {
                query = query.Where(w => w.LocationId == locationId.Value);
            }

            if (cameraId.HasValue && cameraId.Value != Guid.Empty)
            {
                query = query.Where(w => w.CameraId == cameraId.Value);
            }

            var list = await query
                .Include(w => w.LocationRef)
                .Include(w => w.Camera)
                .Include(w => w.WorkerAssignments)
                .OrderBy(w => w.Code)
                .ToListAsync();

            return list.Select(w => MapToResponse(w)).ToList();
        }

        public async Task<WorkstationDetailResponse?> GetWorkstationByIdAsync(Guid tenantId, Guid id)
        {
            var workstation = await _db.Workstations
                .AsNoTracking()
                .Include(w => w.LocationRef)
                .Include(w => w.Camera)
                .Include(w => w.WorkerAssignments)
                    .ThenInclude(a => a.Employee)
                .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id && !w.IsDeleted);

            if (workstation == null) return null;

            var activeSession = await _db.ReliefSessions
                .AsNoTracking()
                .Include(r => r.Camera)
                .Include(r => r.PrimaryEmployee)
                .Include(r => r.RelieverEmployee)
                .Where(r => r.WorkstationId == id && (r.Status == ReliefSessionStatus.PendingHandover || r.Status == ReliefSessionStatus.ActiveRelief))
                .OrderByDescending(r => r.PrimaryLeftAt)
                .FirstOrDefaultAsync();

            var resp = new WorkstationDetailResponse
            {
                Id = workstation.Id,
                TenantId = workstation.TenantId,
                LocationId = workstation.LocationId,
                LocationName = workstation.LocationRef?.Name,
                CameraId = workstation.CameraId,
                CameraName = workstation.Camera?.Name ?? "Unknown Camera",
                CameraExternalId = workstation.Camera?.CameraId ?? string.Empty,
                Name = workstation.Name,
                Code = workstation.Code,
                PolygonJson = workstation.PolygonJson,
                RequiredPrimaryWorkers = workstation.RequiredPrimaryWorkers,
                MaxRelievers = workstation.MaxRelievers,
                HandoverThresholdSeconds = workstation.HandoverThresholdSeconds,
                MaxReliefDurationSeconds = workstation.MaxReliefDurationSeconds,
                CurrentStatus = workstation.CurrentStatus,
                IsActive = workstation.IsActive,
                OperatingScheduleJson = workstation.OperatingScheduleJson,
                PrimaryWorkersCount = workstation.WorkerAssignments.Count(a => a.IsActive && a.Role == WorkstationWorkerRole.Primary),
                RelieversCount = workstation.WorkerAssignments.Count(a => a.IsActive && a.Role == WorkstationWorkerRole.Reliever),
                CreatedAt = workstation.CreatedAt,
                UpdatedAt = workstation.UpdatedAt,
                Assignments = workstation.WorkerAssignments.Select(a => new WorkstationAssignmentResponse
                {
                    Id = a.Id,
                    EmployeeId = a.EmployeeId,
                    EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}".Trim() : "Unknown",
                    EmployeeExternalId = a.Employee?.EmployeeId ?? string.Empty,
                    Role = a.Role,
                    ShiftStartTime = a.ShiftStartTime,
                    ShiftEndTime = a.ShiftEndTime,
                    IsActive = a.IsActive
                }).ToList(),
                ActiveSession = activeSession != null ? MapSessionToResponse(activeSession, workstation) : null
            };

            return resp;
        }

        public async Task<WorkstationResponse> CreateWorkstationAsync(Guid tenantId, CreateWorkstationRequest request)
        {
            // Verify camera belongs to tenant
            var camera = await _db.Cameras
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == request.CameraId && !c.IsDeleted);

            if (camera == null)
            {
                throw new InvalidOperationException($"Camera with ID {request.CameraId} not found for this tenant.");
            }

            // Check code uniqueness
            var codeExists = await _db.Workstations
                .AnyAsync(w => w.TenantId == tenantId && w.Code.ToLower() == request.Code.Trim().ToLower() && !w.IsDeleted);

            if (codeExists)
            {
                throw new InvalidOperationException($"A workstation with code '{request.Code}' already exists for this tenant.");
            }

            if (!string.IsNullOrWhiteSpace(request.OperatingScheduleJson))
            {
                RuleConfigurationValidator.ValidateOperatingSchedule(request.OperatingScheduleJson);
            }

            var workstation = new Workstation
            {
                TenantId = tenantId,
                LocationId = request.LocationId ?? camera.LocationId,
                CameraId = request.CameraId,
                Name = request.Name.Trim(),
                Code = request.Code.Trim().ToUpperInvariant(),
                PolygonJson = string.IsNullOrWhiteSpace(request.PolygonJson) ? "[]" : request.PolygonJson,
                OperatingScheduleJson = string.IsNullOrWhiteSpace(request.OperatingScheduleJson) ? null : request.OperatingScheduleJson,
                RequiredPrimaryWorkers = Math.Max(1, request.RequiredPrimaryWorkers),
                MaxRelievers = Math.Max(1, request.MaxRelievers),
                HandoverThresholdSeconds = Math.Max(5, request.HandoverThresholdSeconds),
                MaxReliefDurationSeconds = Math.Max(30, request.MaxReliefDurationSeconds),
                CurrentStatus = WorkstationStatus.Staffed,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Workstations.Add(workstation);

            // Add worker assignments if provided
            if (request.PrimaryEmployeeIds != null)
            {
                foreach (var empId in request.PrimaryEmployeeIds.Distinct())
                {
                    workstation.WorkerAssignments.Add(new WorkstationWorkerAssignment
                    {
                        TenantId = tenantId,
                        WorkstationId = workstation.Id,
                        EmployeeId = empId,
                        Role = WorkstationWorkerRole.Primary,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (request.RelieverEmployeeIds != null)
            {
                foreach (var empId in request.RelieverEmployeeIds.Distinct())
                {
                    if (request.PrimaryEmployeeIds == null || !request.PrimaryEmployeeIds.Contains(empId))
                    {
                        workstation.WorkerAssignments.Add(new WorkstationWorkerAssignment
                        {
                            TenantId = tenantId,
                            WorkstationId = workstation.Id,
                            EmployeeId = empId,
                            Role = WorkstationWorkerRole.Reliever,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Created workstation {Code} ({Name}) for tenant {TenantId}", workstation.Code, workstation.Name, tenantId);

            TriggerVisionServiceReload();

            workstation.Camera = camera;
            return MapToResponse(workstation);
        }

        public async Task<WorkstationResponse?> UpdateWorkstationAsync(Guid tenantId, Guid id, UpdateWorkstationRequest request)
        {
            var workstation = await _db.Workstations
                .Include(w => w.Camera)
                .Include(w => w.LocationRef)
                .Include(w => w.WorkerAssignments)
                .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id && !w.IsDeleted);

            if (workstation == null) return null;

            if (!string.IsNullOrWhiteSpace(request.Name)) workstation.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Code)) workstation.Code = request.Code.Trim().ToUpperInvariant();
            if (request.LocationId.HasValue) workstation.LocationId = request.LocationId.Value == Guid.Empty ? null : request.LocationId;
            if (request.CameraId.HasValue && request.CameraId.Value != Guid.Empty) workstation.CameraId = request.CameraId.Value;
            if (request.PolygonJson != null) workstation.PolygonJson = request.PolygonJson;
            if (request.RequiredPrimaryWorkers.HasValue) workstation.RequiredPrimaryWorkers = Math.Max(1, request.RequiredPrimaryWorkers.Value);
            if (request.MaxRelievers.HasValue) workstation.MaxRelievers = Math.Max(1, request.MaxRelievers.Value);
            if (request.HandoverThresholdSeconds.HasValue) workstation.HandoverThresholdSeconds = Math.Max(5, request.HandoverThresholdSeconds.Value);
            if (request.MaxReliefDurationSeconds.HasValue) workstation.MaxReliefDurationSeconds = Math.Max(30, request.MaxReliefDurationSeconds.Value);
            if (request.OperatingScheduleJson != null)
            {
                if (!string.IsNullOrWhiteSpace(request.OperatingScheduleJson))
                {
                    RuleConfigurationValidator.ValidateOperatingSchedule(request.OperatingScheduleJson);
                }
                workstation.OperatingScheduleJson = string.IsNullOrWhiteSpace(request.OperatingScheduleJson) ? null : request.OperatingScheduleJson;
            }
            if (request.IsActive.HasValue) workstation.IsActive = request.IsActive.Value;

            workstation.UpdatedAt = DateTime.UtcNow;

            // Sync Worker Assignments if lists passed
            if (request.PrimaryEmployeeIds != null || request.RelieverEmployeeIds != null)
            {
                _db.WorkstationWorkerAssignments.RemoveRange(workstation.WorkerAssignments);

                var newAssignments = new List<WorkstationWorkerAssignment>();
                if (request.PrimaryEmployeeIds != null)
                {
                    foreach (var empId in request.PrimaryEmployeeIds.Distinct())
                    {
                        newAssignments.Add(new WorkstationWorkerAssignment
                        {
                            TenantId = tenantId,
                            WorkstationId = workstation.Id,
                            EmployeeId = empId,
                            Role = WorkstationWorkerRole.Primary,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                if (request.RelieverEmployeeIds != null)
                {
                    foreach (var empId in request.RelieverEmployeeIds.Distinct())
                    {
                        if (request.PrimaryEmployeeIds == null || !request.PrimaryEmployeeIds.Contains(empId))
                        {
                            newAssignments.Add(new WorkstationWorkerAssignment
                            {
                            TenantId = tenantId,
                            WorkstationId = workstation.Id,
                            EmployeeId = empId,
                            Role = WorkstationWorkerRole.Reliever,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        }
                    }
                }

                _db.WorkstationWorkerAssignments.AddRange(newAssignments);
            }

            await _db.SaveChangesAsync();
            TriggerVisionServiceReload();
            return MapToResponse(workstation);
        }

        public async Task<bool> DeleteWorkstationAsync(Guid tenantId, Guid id)
        {
            var workstation = await _db.Workstations
                .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id && !w.IsDeleted);

            if (workstation == null) return false;

            workstation.IsDeleted = true;
            workstation.DeletedAt = DateTime.UtcNow;
            workstation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TriggerVisionServiceReload();
            return true;
        }

        public async Task<List<WorkstationAssignmentResponse>> AssignWorkersAsync(
            Guid tenantId, Guid workstationId, AssignWorkstationWorkersRequest request)
        {
            var workstation = await _db.Workstations
                .Include(w => w.WorkerAssignments)
                .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == workstationId && !w.IsDeleted);

            if (workstation == null)
            {
                throw new KeyNotFoundException("Workstation not found");
            }

            _db.WorkstationWorkerAssignments.RemoveRange(workstation.WorkerAssignments);

            var newAssignments = request.Assignments.Select(item => new WorkstationWorkerAssignment
            {
                TenantId = tenantId,
                WorkstationId = workstationId,
                EmployeeId = item.EmployeeId,
                Role = item.Role,
                ShiftStartTime = item.ShiftStartTime,
                ShiftEndTime = item.ShiftEndTime,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _db.WorkstationWorkerAssignments.AddRange(newAssignments);
            await _db.SaveChangesAsync();
            TriggerVisionServiceReload();

            var loadedAssignments = await _db.WorkstationWorkerAssignments
                .Include(a => a.Employee)
                .Where(a => a.WorkstationId == workstationId)
                .ToListAsync();

            return loadedAssignments.Select(a => new WorkstationAssignmentResponse
            {
                Id = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}".Trim() : "Unknown",
                EmployeeExternalId = a.Employee?.EmployeeId ?? string.Empty,
                Role = a.Role,
                ShiftStartTime = a.ShiftStartTime,
                ShiftEndTime = a.ShiftEndTime,
                IsActive = a.IsActive
            }).ToList();
        }

        private static WorkstationResponse MapToResponse(Workstation w)
        {
            return new WorkstationResponse
            {
                Id = w.Id,
                TenantId = w.TenantId,
                LocationId = w.LocationId,
                LocationName = w.LocationRef?.Name,
                CameraId = w.CameraId,
                CameraName = w.Camera?.Name ?? "Camera",
                CameraExternalId = w.Camera?.CameraId ?? string.Empty,
                Name = w.Name,
                Code = w.Code,
                PolygonJson = w.PolygonJson,
                RequiredPrimaryWorkers = w.RequiredPrimaryWorkers,
                MaxRelievers = w.MaxRelievers,
                HandoverThresholdSeconds = w.HandoverThresholdSeconds,
                MaxReliefDurationSeconds = w.MaxReliefDurationSeconds,
                CurrentStatus = w.CurrentStatus,
                IsActive = w.IsActive,
                OperatingScheduleJson = w.OperatingScheduleJson,
                PrimaryWorkersCount = w.WorkerAssignments?.Count(a => a.IsActive && a.Role == WorkstationWorkerRole.Primary) ?? 0,
                RelieversCount = w.WorkerAssignments?.Count(a => a.IsActive && a.Role == WorkstationWorkerRole.Reliever) ?? 0,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt
            };
        }

        private static ReliefSessionResponse MapSessionToResponse(ReliefSession r, Workstation w)
        {
            return new ReliefSessionResponse
            {
                Id = r.Id,
                WorkstationId = r.WorkstationId,
                WorkstationName = w.Name,
                WorkstationCode = w.Code,
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
            };
        }
    }
}
