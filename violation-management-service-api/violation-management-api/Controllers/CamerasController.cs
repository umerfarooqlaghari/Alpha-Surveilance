using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using AlphaSurveilance.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;

namespace violation_management_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CamerasController(
        ICameraService cameraService, 
        ILogger<CamerasController> logger,
        ICurrentTenantService currentTenantService,
        AppViolationDbContext dbContext,
        IEncryptionService encryptionService) : ControllerBase
    {
        private Guid GetTenantId()
        {
            var tenantId = currentTenantService.TenantId;
            if (!tenantId.HasValue)
            {
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            }
            return tenantId.Value;
        }

        /// <summary>
        /// Create a new camera for a tenant
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> CreateCamera([FromBody] CreateCameraRequest request)
        {
            try
            {
                // Enforce tenant context
                if (currentTenantService.IsSuperAdmin)
                {
                    if (request.TenantId == Guid.Empty)
                    {
                        return BadRequest(new { error = "Tenant ID is required for Super Admin when creating a camera." });
                    }
                }
                else
                {
                    request.TenantId = GetTenantId();

                    // Only SuperAdmin is allowed to define policy rule configurations
                    // (geofences, anomaly thresholds, etc.). Strip any client-supplied
                    // RuleConfigurationJson from non-SuperAdmin callers as defense in
                    // depth — the UI hides the editor, but the API must enforce too.
                    if (request.ActiveViolations != null)
                    {
                        foreach (var v in request.ActiveViolations)
                        {
                            v.RuleConfigurationJson = null;
                        }
                    }
                }

                var camera = await cameraService.CreateCameraAsync(request);
                return CreatedAtAction(nameof(GetCamera), new { id = camera.Id }, camera);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating camera");
                return StatusCode(500, new { error = "An error occurred while creating the camera" });
            }
        }

        /// <summary>
        /// Get all cameras for a tenant
        /// </summary>
        [HttpGet]
        [Authorize] // Allow TenantAdmin and SuperAdmin
        public async Task<IActionResult> GetCamerasByTenant([FromQuery] Guid? tenantId, [FromQuery] Guid? locationId)
        {
            try
            {
                Guid targetTenantId;

                if (currentTenantService.IsSuperAdmin)
                {
                    if (!tenantId.HasValue) return BadRequest(new { error = "Tenant ID is required for Super Admin" });
                    targetTenantId = tenantId.Value;
                }
                else
                {
                    targetTenantId = GetTenantId();
                }

                var cameras = await cameraService.GetCamerasByTenantAsync(targetTenantId, locationId);
                return Ok(cameras);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching cameras");
                return StatusCode(500, new { error = "An error occurred while fetching cameras" });
            }
        }

        /// <summary>
        /// Get camera by ID
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> GetCamera(Guid id)
        {
            try
            {
                var camera = await cameraService.GetCameraByIdAsync(id);
                if (camera == null)
                    return NotFound(new { error = "Camera not found" });

                // Ensure TenantAdmin can only access their own cameras
                if (!currentTenantService.IsSuperAdmin)
                {
                    var tenantId = GetTenantId();
                    if (camera.TenantId != tenantId) return Forbid();
                }

                return Ok(camera);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching camera {CameraId}", id);
                return StatusCode(500, new { error = "An error occurred while fetching the camera" });
            }
        }

        /// <summary>
        /// Get decrypted RTSP URL for a camera (internal use only)
        /// </summary>
        [HttpGet("{id}/rtsp-url")]
        [Authorize(Policy = "SuperAdmin")]
        public async Task<IActionResult> GetRtspUrl(Guid id)
        {
            try
            {
                var rtspUrl = await cameraService.GetDecryptedRtspUrlAsync(id);
                if (rtspUrl == null)
                    return NotFound(new { error = "Camera not found" });

                return Ok(new { rtspUrl });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching RTSP URL for camera {CameraId}", id);
                return StatusCode(500, new { error = "An error occurred while fetching the RTSP URL" });
            }
        }

        /// <summary>
        /// Temporary endpoint to clear outbox backlog
        /// </summary>
        [HttpDelete("internal/clear-outbox")]
        [AllowAnonymous]
        public async Task<IActionResult> ClearOutboxInternal()
        {
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE \"OutboxMessages\" SET \"ProcessedAt\" = NOW() WHERE \"ProcessedAt\" IS NULL");
            return Ok(new { message = "Backlog cleared" });
        }

        /// <summary>
        /// [SERVICE-TO-SERVICE] Returns all Active cameras with decrypted RTSP URLs.
        /// Protected by X-Internal-Api-Key header middleware — NOT JWT.
        /// Called exclusively by the Vision Inference Service at startup and on hot-reload.
        ///
        /// When <paramref name="deviceId"/> is supplied the result is filtered to:
        ///   - cameras directly assigned to that device  (Camera.DeviceId == deviceId), PLUS
        ///   - cameras in the device's tenant with no device assignment (Camera.DeviceId IS NULL,
        ///     "shared pool" — preserves backward compatibility with single-device tenants).
        /// When <paramref name="deviceId"/> is omitted the legacy behaviour returns every active
        /// camera (used in dev / single-device deployments without explicit device registration).
        /// </summary>
        [HttpGet("internal/active")]
        [AllowAnonymous] // Auth handled by InternalApiKeyMiddleware before this point
        public async Task<IActionResult> GetActiveCamerasInternal([FromQuery] Guid? deviceId = null)
        {
            try
            {
                var allCamerasCount = await dbContext.Cameras.CountAsync();
                var activeCamerasCount = await dbContext.Cameras.CountAsync(c => c.Status == CameraStatus.Active);
                
                logger.LogInformation("[Internal] Total cameras in DB: {Total}, Active: {Active}, DeviceId: {DeviceId}",
                    allCamerasCount, activeCamerasCount, deviceId?.ToString() ?? "(none)");

                Guid? deviceTenantId = null;
                if (deviceId.HasValue)
                {
                    var device = await dbContext.EdgeDevices
                        .Where(d => d.Id == deviceId.Value && !d.IsDeleted)
                        .Select(d => new { d.TenantId, d.Status })
                        .FirstOrDefaultAsync();

                    if (device == null)
                    {
                        logger.LogWarning("[Internal] Unknown deviceId {DeviceId} — returning empty camera list", deviceId);
                        return Ok(Array.Empty<InternalCameraDto>());
                    }
                    if (device.Status != EdgeDeviceStatus.Active)
                    {
                        logger.LogInformation("[Internal] Device {DeviceId} is disabled — returning empty camera list", deviceId);
                        return Ok(Array.Empty<InternalCameraDto>());
                    }
                    deviceTenantId = device.TenantId;

                    // Refresh LastSeenAt on every poll — piggy-backs on the fetch
                    // so we don't need a separate background timer in the vision service.
                    await dbContext.EdgeDevices
                        .Where(d => d.Id == deviceId.Value)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, DateTime.UtcNow));
                }

                var query = dbContext.Cameras
                    .Include(c => c.Tenant)
                    .Include(c => c.LocationRef)
                    .Include(c => c.ActiveViolationTypes)
                        .ThenInclude(v => v.SopViolationType)
                            .ThenInclude(sv => sv.AiModel)
                    .Include(c => c.DetectionSchedules)
                    .Where(c => c.Status == CameraStatus.Active && c.IsDetectionEnabled);

                if (deviceTenantId.HasValue)
                {
                    var tenantId = deviceTenantId.Value;
                    query = query.Where(c =>
                        c.TenantId == tenantId &&
                        (c.DeviceId == deviceId!.Value || c.DeviceId == null));
                }

                var cameras = await query.AsNoTracking().ToListAsync();

                var utcNow = DateTime.UtcNow;

                var result = cameras
                    .Where(c => !c.DetectionSchedules.Any(s => s.IsActive && IsInSleepWindow(s, utcNow)))
                    .Select(c =>
                {
                    string decryptedUrl;
                    try
                    {
                        decryptedUrl = encryptionService.Decrypt(c.RtspUrlEncrypted);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("[Internal] Decryption FAILED for camera {CameraId} ({Name}). Error: {Message}", c.CameraId, c.Name, ex.Message);
                        decryptedUrl = string.Empty;
                    }

                    return new InternalCameraDto
                    {
                        Id = c.Id,
                        CameraId = c.CameraId,
                        TenantId = c.TenantId,
                        TenantName = c.Tenant != null ? c.Tenant.TenantName : string.Empty,
                        Name = c.Name,
                        Location = c.Location,
                        LocationId = c.LocationId,
                        LocationName = c.LocationRef != null ? c.LocationRef.Name : null,
                        LocationCode = c.LocationRef != null ? c.LocationRef.Code : null,
                        RtspUrl = decryptedUrl,
                        WhipUrl = c.WhipUrl,
                        IsStreaming = c.IsStreaming,
                        TargetFps = c.TargetFps > 0 ? c.TargetFps : 1.0,
                        IsDetectionEnabled = c.IsDetectionEnabled,
                        DetectionSchedules = c.DetectionSchedules.Select(s => new DetectionScheduleDto
                        {
                            Id = s.Id,
                            DaysOfWeek = s.DaysOfWeek,
                            StartTime = s.StartTime.ToString("HH:mm"),
                            EndTime = s.EndTime.ToString("HH:mm"),
                            Label = s.Label,
                            IsActive = s.IsActive
                        }).ToList(),
                        ViolationRules = c.ActiveViolationTypes
                             .Where(v => v.SopViolationType != null)
                             .Select(v => new ViolationRuleDto
                             {
                                 SopViolationTypeId = v.SopViolationTypeId,
                                 ModelIdentifier = v.SopViolationType.ModelIdentifier,
                                 TriggerLabels = !string.IsNullOrWhiteSpace(v.TriggerLabels)
                                     ? v.TriggerLabels
                                     : v.SopViolationType.TriggerLabels ?? string.Empty,
                                 RuleConfigurationJson = v.RuleConfigurationJson,
                                 // AiModel registry fields
                                 AiModelId        = v.SopViolationType.AiModelId,
                                 ModelStatus       = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.Status.ToString()
                                     : "Available",
                                 ModelType         = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.ModelType.ToString()
                                     : "YoloLocal",
                                 ModelDownloadUrl  = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.DownloadUrl
                                     : null,
                                 ModelS3Bucket     = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.S3Bucket
                                     : null,
                                 ModelS3Key        = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.S3Key
                                     : null,
                                 ModelLocalPath    = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.LocalPath
                                     : null,
                                 ModelSha256       = v.SopViolationType.AiModel != null
                                     ? v.SopViolationType.AiModel.Sha256Checksum
                                     : null,
                             })
                             .ToList()
                    };
                })
                .Where(c => !string.IsNullOrEmpty(c.RtspUrl))
                .ToList();

                logger.LogInformation("[Internal] Returning {Count} active cameras to Vision Service after decryption filtering", result.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Internal] Error fetching active cameras for Vision Service");
                return StatusCode(500, new { error = "Failed to retrieve active cameras" });
            }
        }

        /// <summary>
        /// Update camera
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> UpdateCamera(Guid id, [FromBody] UpdateCameraRequest request)
        {
            try
            {
                // Verify ownership first (optional optimization, service might do it)
                 if (!currentTenantService.IsSuperAdmin)
                {
                    var existing = await cameraService.GetCameraByIdAsync(id);
                    if (existing == null) return NotFound();
                    if (existing.TenantId != GetTenantId()) return Forbid();

                    // Only SuperAdmin can define policy rule configurations.
                    // Tenant admins may still edit violation assignments / trigger labels,
                    // but each assignment's RuleConfigurationJson is forced to the value
                    // currently stored in the DB (no create, no edit, no wipe).
                    if (request.ActiveViolations != null)
                    {
                        var existingByVid = existing.ActiveViolations
                            .GroupBy(v => v.SopViolationTypeId)
                            .ToDictionary(g => g.Key, g => g.First().RuleConfigurationJson);

                        foreach (var v in request.ActiveViolations)
                        {
                            v.RuleConfigurationJson = existingByVid.TryGetValue(v.SopViolationTypeId, out var cfg)
                                ? cfg
                                : null;
                        }
                    }
                }

                var camera = await cameraService.UpdateCameraAsync(id, request);
                if (camera == null)
                    return NotFound(new { error = "Camera not found" });

                return Ok(camera);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating camera {CameraId}", id);
                return StatusCode(500, new { error = "An error occurred while updating the camera" });
            }
        }

        /// <summary>
        /// Update camera status
        /// </summary>
        [HttpPatch("{id}/status")]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> UpdateCameraStatus(Guid id, [FromBody] UpdateCameraStatusRequest request)
        {
            try
            {
                if (!Enum.IsDefined(typeof(CameraStatus), request.Status))
                    return BadRequest(new { error = "Invalid status value" });

                // Verify ownership
                 if (!currentTenantService.IsSuperAdmin)
                {
                    var existing = await cameraService.GetCameraByIdAsync(id);
                    if (existing == null) return NotFound();
                    if (existing.TenantId != GetTenantId()) return Forbid();
                }

                var camera = await cameraService.UpdateCameraStatusAsync(id, (CameraStatus)request.Status);
                if (camera == null)
                    return NotFound(new { error = "Camera not found" });

                return Ok(camera);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating camera status {CameraId}", id);
                return StatusCode(500, new { error = "An error occurred while updating camera status" });
            }
        }

        /// <summary>
        /// Delete camera
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize] // Allow Tenant Admin to delete? Policy was SuperAdmin, checking...
        public async Task<IActionResult> DeleteCamera(Guid id)
        {
            try
            {
                // Verify ownership if not super admin
                 if (!currentTenantService.IsSuperAdmin)
                {
                    var existing = await cameraService.GetCameraByIdAsync(id);
                    if (existing == null) return NotFound();
                    if (existing.TenantId != GetTenantId()) return Forbid();
                }

                var result = await cameraService.DeleteCameraAsync(id);
                if (!result)
                    return NotFound(new { error = "Camera not found" });

                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting camera {CameraId}", id);
                return StatusCode(500, new { error = "An error occurred while deleting the camera" });
            }
        }

        /// <summary>
        /// Returns true when <paramref name="utcNow"/> falls inside the sleep window defined by
        /// <paramref name="schedule"/>. Handles overnight windows (StartTime &gt; EndTime).
        /// DaysOfWeek = 0 or 127 means "every day"; otherwise uses .NET DayOfWeek bit values
        /// (Sunday=1, Monday=2, Tuesday=4, Wednesday=8, Thursday=16, Friday=32, Saturday=64).
        /// </summary>
        private static bool IsInSleepWindow(DetectionSchedule schedule, DateTime utcNow)
        {
            // Check day-of-week applicability
            if (schedule.DaysOfWeek != 0 && schedule.DaysOfWeek != 127)
            {
                var dayBit = 1 << (int)utcNow.DayOfWeek; // Sunday=1, Monday=2, ...
                if ((schedule.DaysOfWeek & dayBit) == 0)
                    return false;
            }

            var current = TimeOnly.FromDateTime(utcNow);

            if (schedule.StartTime <= schedule.EndTime)
            {
                // Normal (intra-day) window, e.g. 08:00–12:00
                return current >= schedule.StartTime && current < schedule.EndTime;
            }
            else
            {
                // Overnight window, e.g. 22:00 → 06:00 next day
                return current >= schedule.StartTime || current < schedule.EndTime;
            }
        }
    }
}
