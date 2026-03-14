using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using violation_management_api.Services.Interfaces; // Fixed namespace for ICameraService
using AlphaSurveilance.Services.Interfaces; // For IViolationService
using AlphaSurveilance.Core.Exceptions;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;

namespace AlphaSurveilance.Services
{
    public class ViolationService(
        IViolationRepository repository,
        ICameraService cameraService,
        IMapper mapper,
        IMemoryCache memoryCache,
        IServiceScopeFactory scopeFactory,
        ILogger<ViolationService> logger) : IViolationService
    {
        public async Task<ViolationResponse?> GetViolationAsync(Guid id, string tenantId)
        {
            if (!Guid.TryParse(tenantId, out var tenantGuid)) return null;
            var violation = await repository.GetByIdAsync(id, tenantGuid);
            if (violation == null) return null;

            var response = mapper.Map<ViolationResponse>(violation);
            
            // Enrich camera name
            if (!string.IsNullOrEmpty(response.CameraId))
            {
                var cameras = await cameraService.GetCamerasByTenantAsync(tenantGuid);
                var camera = cameras.FirstOrDefault(c => 
                    string.Equals(c.CameraId, response.CameraId, StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(c.Id.ToString(), response.CameraId, StringComparison.OrdinalIgnoreCase));
                
                if (camera != null) response.CameraName = camera.Name;
            }

            return response;
        }

        public async Task<IEnumerable<ViolationResponse>> GetViolationsAsync(string tenantId)
        {
            if (!Guid.TryParse(tenantId, out var tenantGuid)) return Enumerable.Empty<ViolationResponse>();
            
            var violations = await repository.GetAllAsync(tenantGuid);
            var responses = mapper.Map<IEnumerable<ViolationResponse>>(violations).ToList();

            if (responses.Any())
            {
                // Fetch cameras for the tenant to build a lookup map
                var cameras = await cameraService.GetCamerasByTenantAsync(tenantGuid);
                
                // Map both CameraId (user string) and Id (Guid string) to the Name
                var cameraMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cam in cameras)
                {
                    if (!string.IsNullOrEmpty(cam.CameraId)) cameraMap[cam.CameraId] = cam.Name;
                    cameraMap[cam.Id.ToString()] = cam.Name;
                }

                foreach (var response in responses)
                {
                    if (response.CameraId != null && cameraMap.TryGetValue(response.CameraId, out var name))
                    {
                        response.CameraName = name;
                    }
                }
            }

            return responses;
        }

        public async Task<ViolationStatsResponse> GetStatsAsync(string tenantId)
        {
            // Fix: Parse string tenantId to Guid and use correct method name
            if (!Guid.TryParse(tenantId, out var tenantGuid))
            {
                 // Handle invalid guid if necessary, or just throw/return empty
                 // For now assuming valid guid string
                 throw new ArgumentException("Invalid Tenant ID format");
            }

            var (activeViolations, resolvedToday) = await repository.GetStatsAsync(tenantGuid);
            
            var cameras = await cameraService.GetCamerasByTenantAsync(tenantGuid);
            var totalCameras = cameras.Count;

            return new ViolationStatsResponse
            {
                TotalCameras = totalCameras,
                ActiveViolations = activeViolations,
                ResolvedToday = resolvedToday
            };
        }

        public async Task<AlphaSurveilance.DTOs.Responses.AnalyticsResponse> GetAnalyticsAsync(string tenantId, DateTime? startDate = null, DateTime? endDate = null, string? cameraId = null)
        {
            if (!Guid.TryParse(tenantId, out var tenantGuid))
            {
                throw new ArgumentException("Invalid Tenant ID format");
            }
            return await repository.GetAnalyticsAsync(tenantGuid, startDate, endDate, cameraId);
        }

        public async Task<ViolationResponse> CreateViolationAsync(ViolationRequest request)
        {
            var violation = mapper.Map<Violation>(request);

            // Generate Outbox messages for external actions
            var outboxMessages = await CreateOutboxMessages(violation);

            await repository.AddAsync(violation);
            if (outboxMessages.Any())
            {
                await repository.AddOutboxMessagesAsync(outboxMessages);
            }
            
            await repository.SaveChangesAsync();

            return mapper.Map<ViolationResponse>(violation);
        }

        public async Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationPayload> requests)
        {
            if (requests == null || !requests.Any()) return 0;

            var validRequests = requests.Where(r => 
                !string.IsNullOrWhiteSpace(r.CorrelationId) && 
                !string.IsNullOrWhiteSpace(r.TenantId)).ToList();

            if (!validRequests.Any()) return 0;

            var correlationIds = validRequests.Select(r => r.CorrelationId).Distinct();
            var existingIds = await repository.GetExistingCorrelationIdsAsync(correlationIds);
            
            var newRequests = validRequests.Where(r => !existingIds.Contains(r.CorrelationId)).ToList();
            if (!newRequests.Any()) return 0;

            // Resolve SOP Violation Types and Cameras in bulk for enrichment
            var modelIdentifiers = newRequests.Select(r => r.ModelIdentifier).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
            var tenantIds = newRequests.Select(r => Guid.Parse(r.TenantId)).Distinct().ToList();
            var cameraIds = newRequests.Select(r => r.CameraId).Distinct().ToList();
            var cameraGuids = cameraIds
                .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppViolationDbContext>();
            
            var sopViolationTypes = await db.SopViolationTypes
                .Include(sv => sv.Sop)
                .Where(sv => modelIdentifiers.Contains(sv.ModelIdentifier))
                .ToListAsync();

            var cameras = await db.Cameras
                .Where(c => tenantIds.Contains(c.TenantId) && (cameraIds.Contains(c.CameraId) || cameraGuids.Contains(c.Id)))
                .Select(c => new { c.Id, c.TenantId, c.CameraId, c.Name })
                .ToListAsync();

            // Build a flexible mapping dictionary
            var cameraMapping = new Dictionary<(Guid TenantId, string identifier), string>();
            foreach (var c in cameras)
            {
                if (!string.IsNullOrEmpty(c.CameraId))
                    cameraMapping[(c.TenantId, c.CameraId.ToLower())] = c.Name;
                
                cameraMapping[(c.TenantId, c.Id.ToString().ToLower())] = c.Name;
            }

            var violations = new List<Violation>();
            foreach (var req in newRequests)
            {
                var v = mapper.Map<Violation>(req);
                var svType = sopViolationTypes.FirstOrDefault(sv => sv.ModelIdentifier == req.ModelIdentifier);
                if (svType != null)
                {
                    v.SopViolationTypeId = svType.Id;
                    v.SopViolationType = svType; // Attach for outbox enrichment
                }

                // Add Camera Name to Metadata or use a temporary property if needed
                // For now, we'll pass it to CreateOutboxMessages via a dictionary or similar 
                // but since Violation is a Domain object, let's just ensure we have enough info.
                
                violations.Add(v);
            }

            // We need to pass the camera names to SaveBulkWithOutboxAsync
            await SaveBulkWithOutboxAsync(violations, cameraMapping);
            
            return violations.Count;
        }

        private async Task SaveBulkWithOutboxAsync(List<Violation> violations, Dictionary<(Guid TenantId, string identifier), string>? cameraNames = null)
        {
            var allOutboxMessages = new List<OutboxMessage>();

            foreach (var violation in violations)
            {
                string? cameraName = null;
                if (violation.CameraId != null)
                {
                    cameraNames?.TryGetValue((violation.TenantId, violation.CameraId.ToLower()), out cameraName);
                }
                allOutboxMessages.AddRange(await CreateOutboxMessages(violation, cameraName));

                // CRITICAL FIX: Detach the navigation property right before AddRangeAsync
                // The 'db' context that fetched 'svType' runs on a distinct background scope.
                // If we submit the attached complex object to our domain 'repository'DbContext, 
                // EF Core will mistakenly think it's a completely newborn entity and attempt a Duplicate Insert.
                violation.SopViolationType = null;
            }

            await repository.AddRangeAsync(violations);
            if (allOutboxMessages.Any())
            {
                await repository.AddOutboxMessagesAsync(allOutboxMessages);
            }

            await repository.SaveChangesAsync();
        }
        public async Task<int> ProcessViolationsBulkAsync(IEnumerable<ViolationRequest> requests)
        {
            if (requests == null || !requests.Any()) return 0;

            var validRequests = requests.Where(r => 
                !string.IsNullOrWhiteSpace(r.CorrelationId) && 
                !string.IsNullOrWhiteSpace(r.TenantId)).ToList();

            if (!validRequests.Any()) return 0;

            var correlationIds = validRequests.Select(r => r.CorrelationId).Distinct();
            var existingIds = await repository.GetExistingCorrelationIdsAsync(correlationIds);
            
            var newRequests = validRequests.Where(r => !existingIds.Contains(r.CorrelationId)).ToList();
            if (!newRequests.Any()) return 0;

            var violations = mapper.Map<IEnumerable<Violation>>(newRequests).ToList();
            await SaveBulkWithOutboxAsync(violations);
            
            return violations.Count;
        }

        private async Task<List<OutboxMessage>> CreateOutboxMessages(Violation violation, string? cameraName = null)
        {
            var messages = new List<OutboxMessage>();

            // Resolve human-readable names for UI
            var sopName = violation.SopViolationType?.Sop?.Name ?? "General";
            var violationTypeName = violation.SopViolationType?.Name ?? "Generic";
            var detectedCameraName = cameraName ?? violation.CameraId ?? "Unknown Camera";

            // Outbox for Audit Log
            messages.Add(new OutboxMessage
            {
                Type = "AuditLog",
                Content = JsonSerializer.Serialize(new { 
                    violation.Id, 
                    violation.TenantId, 
                    Type = violationTypeName,
                    Timestamp = violation.Timestamp
                })
            });

            // Outbox for Email Alert
            {
                var cacheKey = $"Alert_{violation.TenantId}_{violation.CameraId}_{violation.SopViolationTypeId}";
                
                if (!memoryCache.TryGetValue(cacheKey, out _))
                {
                    // Fetch tenant notification emails dynamically (fire-and-forget safe via scope)
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppViolationDbContext>();
                    var tenantEmails = await db.TenantNotificationEmails
                        .Where(e => e.TenantId == violation.TenantId && e.IsActive)
                        .Select(e => e.Email)
                        .ToListAsync();

                    if (tenantEmails.Any())
                    {
                        foreach (var recipientEmail in tenantEmails)
                        {
                            messages.Add(new OutboxMessage
                            {
                                Type = "EmailAlert",
                                Content = JsonSerializer.Serialize(new {
                                    Subject = $"🚨 ALERT: {violationTypeName} Detected",
                                    Body = $"A {violationTypeName} violation was detected on camera {detectedCameraName} for tenant {violation.TenantId}. Evidence: {violation.FramePath}"
                                })
                            });
                        }

                        // Set cooldown for 5 minutes per camera/type
                        memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
                    }
                    else
                    {
                        logger.LogWarning("High-severity violation for tenant {TenantId} but no active notification emails configured — skipping email alert.", violation.TenantId);
                    }
                }
            }

            // Outbox for Real-Time UI Notification (WebSockets)
            messages.Add(new OutboxMessage
            {
                Type = "HubNotification",
                Content = JsonSerializer.Serialize(new { 
                    violation.Id, 
                    violation.TenantId, 
                    Timestamp = violation.Timestamp.ToString("O"),
                    violation.FramePath,
                    violation.CameraId,
                    CameraName = detectedCameraName,
                    SopName = sopName,
                    ViolationTypeName = violationTypeName
                })
            });

            return messages;
        }

        public async Task<bool> ProcessViolationAsync(ViolationRequest request)
        {
            try
            {
                ValidateRequest(request);

                // Idempotency check
                if (await repository.ExistsByCorrelationIdAsync(request.CorrelationId))
                {
                    logger.LogWarning("Duplicate violation detected: {CorrelationId}", request.CorrelationId);
                    return true; 
                }

                await CreateViolationAsync(request);
                return true;
            }
            catch (DomainValidationException ex)
            {
                logger.LogError(ex, "Validation failed for violation request");
                // Validation failures are "poison" and should NOT be retried by SQS if possible
                // However, we throw to let the worker decide if it's a poison message
                throw; 
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transient error processing violation");
                return false; // Retriable failure
            }
        }

        private void ValidateRequest(ViolationRequest request)
        {
            if (request == null) throw new DomainValidationException("Request cannot be null");
            if (string.IsNullOrWhiteSpace(request.TenantId)) throw new DomainValidationException("TenantId is required");
            if (string.IsNullOrWhiteSpace(request.CorrelationId)) throw new DomainValidationException("CorrelationId is required");
            // Add more "military grade" validation rules here
        }

    }
}
