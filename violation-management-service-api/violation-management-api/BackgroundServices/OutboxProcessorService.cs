using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.Services.Interfaces;
using AlphaSurveilance.Services;
using AlphaSurveilance.Bff.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.BackgroundServices
{
    public class OutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Outbox Processor Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<IViolationRepository>();
                    var emailDispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcherService>();
                    var auditClient = scope.ServiceProvider.GetRequiredService<IAuditApiClient>();
                    var notificationClient = scope.ServiceProvider.GetRequiredService<NotificationService.NotificationServiceClient>();

                    var messages = await repository.GetUnprocessedOutboxMessagesAsync(20);
                    
                    if (messages.Any())
                    {
                        logger.LogInformation("Found {Count} unprocessed outbox messages. Processing...", messages.Count());
                    }

                    if (!messages.Any())
                    {
                        // logger.LogDebug("No messages found. Sleeping...");
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    foreach (var message in messages)
                    {
                        await ProcessMessageAsync(message, emailDispatcher, auditClient, notificationClient, repository);
                    }

                    await repository.SaveChangesAsync();
                }
                catch (OperationCanceledException)
                {
                    // Clean shutdown
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in Outbox Processor loop");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task ProcessMessageAsync(
            OutboxMessage message, 
            EmailDispatcherService emailDispatcher, 
            IAuditApiClient auditClient,
            NotificationService.NotificationServiceClient notificationClient,
            IViolationRepository repository)
        {
            logger.LogInformation("Processing Outbox Message {Id} (Type: {Type})", message.Id, message.Type);
            try
            {
                bool success = false;

                switch (message.Type)
                {
                    case "EmailAlert":
                        var emailData = JsonSerializer.Deserialize<EmailPayload>(message.Content);
                        if (emailData != null)
                        {
                            // success = await emailDispatcher.SendEmailAsync(new System.Collections.Generic.List<string> { emailData.To }, emailData.Subject, emailData.Body);
                            
                            // Pause SES altogether as requested
                            success = true;
                            logger.LogWarning("SES Email dispatch paused. Skipping email to {To}", emailData.To);
                            
                            // Respect SES Sandbox sending rate limit (max 1 per second)
                            // await Task.Delay(1200);
                        }
                        break;

                    case "AuditLog":
                        var auditData = JsonSerializer.Deserialize<AuditPayload>(message.Content);
                        if (auditData != null)
                        {
                            success = await auditClient.LogViolationAsync(auditData.Id, auditData.TenantId, auditData.Type, auditData.Timestamp, CancellationToken.None);
                        }
                        break;

                    case "HubNotification":
                        var hubData = JsonSerializer.Deserialize<NotificationPayload>(message.Content);
                        if (hubData != null)
                        {
                            // Data Flow: Outbox -> BFF Notification Service (gRPC)
                            var request = new ViolationNotificationRequest
                            {
                                Id = hubData.Id.ToString(),
                                TenantId = hubData.TenantId,
                                Type = hubData.Type,
                                Severity = hubData.Severity,
                                Timestamp = hubData.Timestamp,
                                FramePath = hubData.FramePath ?? "",
                                CameraId = hubData.CameraId ?? "",
                                CameraName = hubData.CameraName ?? "",
                                SopName = hubData.SopName ?? "",
                                ViolationTypeName = hubData.ViolationTypeName ?? ""
                            };
                            var response = await notificationClient.PushViolationAsync(request);
                            success = response.Acknowledged;
                        }
                        break;
                }

                if (success)
                {
                    message.ProcessedAt = DateTime.UtcNow;
                }
                else
                {
                    message.RetryCount++;
                    message.Error = "Action returned failure status";
                    message.LastAttemptAt = DateTime.UtcNow;
                }
                
                // Removed explicit update from here to move it to the end
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);
                message.RetryCount++;
                message.Error = ex.Message;
                message.LastAttemptAt = DateTime.UtcNow;
            }

            // Explicitly mark as modified to ensure persistence (in both success and failure cases)
            await repository.UpdateOutboxMessage(message);
        }

        private class EmailPayload { public string To { get; set; } = string.Empty; public string Subject { get; set; } = string.Empty; public string Body { get; set; } = string.Empty; }
        private class AuditPayload { public Guid Id { get; set; } public string TenantId { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public DateTime Timestamp { get; set; } }
        private class NotificationPayload 
        { 
            public Guid Id { get; set; } 
            public string TenantId { get; set; } = string.Empty; 
            public string Type { get; set; } = string.Empty;
            public string Severity { get; set; } = string.Empty;
            public string Timestamp { get; set; } = string.Empty;
            public string? FramePath { get; set; }
            public string? CameraId { get; set; }
            public string? CameraName { get; set; }
            public string? SopName { get; set; }
            public string? ViolationTypeName { get; set; }
        }
    }
}
