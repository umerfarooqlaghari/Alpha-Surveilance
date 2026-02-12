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

namespace AlphaSurveilance.Services
{
    public class ViolationService(
        IViolationRepository repository,
        ICameraService cameraService,
        IMapper mapper,
        ILogger<ViolationService> logger) : IViolationService
    {
        public async Task<ViolationResponse?> GetViolationAsync(Guid id, string tenantId)
        {
            var violation = await repository.GetByIdAsync(id, tenantId);
            return violation == null ? null : mapper.Map<ViolationResponse>(violation);
        }

        public async Task<IEnumerable<ViolationResponse>> GetViolationsAsync(string tenantId)
        {
            var violations = await repository.GetAllAsync(tenantId);
            return mapper.Map<IEnumerable<ViolationResponse>>(violations);
        }

        public async Task<ViolationStatsResponse> GetStatsAsync(string tenantId)
        {
            var (activeViolations, resolvedToday) = await repository.GetStatsAsync(tenantId);
            
            // Fix: Parse string tenantId to Guid and use correct method name
            if (!Guid.TryParse(tenantId, out var tenantGuid))
            {
                 // Handle invalid guid if necessary, or just throw/return empty
                 // For now assuming valid guid string
                 throw new ArgumentException("Invalid Tenant ID format");
            }

            var cameras = await cameraService.GetCamerasByTenantAsync(tenantGuid);
            var totalCameras = cameras.Count;

            return new ViolationStatsResponse
            {
                TotalCameras = totalCameras,
                ActiveViolations = activeViolations,
                ResolvedToday = resolvedToday
            };
        }

        public async Task<ViolationResponse> CreateViolationAsync(ViolationRequest request)
        {
            var violation = mapper.Map<Violation>(request);

            // Generate Outbox messages for external actions
            var outboxMessages = CreateOutboxMessages(violation);

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

            var violations = mapper.Map<IEnumerable<Violation>>(newRequests).ToList();
            await SaveBulkWithOutboxAsync(violations);
            
            return violations.Count;
        }

        private async Task SaveBulkWithOutboxAsync(List<Violation> violations)
        {
            var allOutboxMessages = new List<OutboxMessage>();

            foreach (var violation in violations)
            {
                allOutboxMessages.AddRange(CreateOutboxMessages(violation));
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

        private List<OutboxMessage> CreateOutboxMessages(Violation violation)
        {
            var messages = new List<OutboxMessage>();

            // Outbox for Audit Log
            messages.Add(new OutboxMessage
            {
                Type = "AuditLog",
                Content = JsonSerializer.Serialize(new { 
                    violation.Id, 
                    violation.TenantId, 
                    Type = violation.Type.ToString() 
                })
            });

            // Outbox for Email Alert
            if (violation.Severity >= ViolationSeverity.High)
            {
                messages.Add(new OutboxMessage
                {
                    Type = "EmailAlert",
                    Content = JsonSerializer.Serialize(new {
                        To = "security-ops@alphasurveillance.com",
                        Subject = $"🚨 ALERT: {violation.Severity} Severity {violation.Type} Detected",
                        Body = $"A {violation.Type} violation was detected on camera {violation.CameraId} for tenant {violation.TenantId}. Evidence: {violation.FramePath}"
                    })
                });
            }

            // Outbox for Real-Time UI Notification (WebSockets)
            messages.Add(new OutboxMessage
            {
                Type = "HubNotification",
                Content = JsonSerializer.Serialize(new { 
                    violation.Id, 
                    violation.TenantId, 
                    Type = violation.Type.ToString(),
                    Severity = (violation.Severity ?? ViolationSeverity.Low).ToString(),
                    Timestamp = violation.Timestamp.ToString("O"),
                    violation.FramePath,
                    violation.CameraId
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
            if (request.Type == ViolationType.Unknown) throw new DomainValidationException("Unknown violation type is not allowed");
            // Add more "military grade" validation rules here
        }

    }
}
