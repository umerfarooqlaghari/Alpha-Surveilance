using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.Services.Interfaces;
using AlphaSurveilance.Core.Exceptions;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace AlphaSurveilance.BackgroundServices
{
    public class ViolationWorkerService(
        IAmazonSQS sqs,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ViolationWorkerService> logger) : BackgroundService
    {
        private const int MaxRetries = 5;
        private const int HeartbeatIntervalSeconds = 30; // Extend visibility every 30s
        private const int VisibilityTimeoutSeconds = 60;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueUrl = config.GetValue<string>("SQSConfig:QueueUrl");
            if (string.IsNullOrEmpty(queueUrl))
            {
                logger.LogError("Queue URL is missing!");
                return;
            }

            logger.LogInformation("Advanced Violation Worker started. Queue: {Queue}", queueUrl);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl = queueUrl,
                        WaitTimeSeconds = 20,
                        MaxNumberOfMessages = 10,
                        VisibilityTimeout = VisibilityTimeoutSeconds,
                        MessageSystemAttributeNames = new List<string> { "ApproximateReceiveCount" }
                    }, stoppingToken);

                    if (response?.Messages?.Count > 0)
                    {
                        // Parallel processing of the batch
                        await ProcessMessageBatchInBulkAsync(queueUrl, response.Messages, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error in worker loop");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task ProcessMessageBatchInBulkAsync(string? queueUrl, List<Message> messages, CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(queueUrl)) return;
            var validMessages = new List<Message>();
            var requests = new List<ViolationPayload>();

            foreach (var message in messages)
            {
                // 1. Poison Message Check
                if (message.Attributes.TryGetValue("ApproximateReceiveCount", out var recvCountStr) &&
                    int.TryParse(recvCountStr, out var recvCount) && recvCount > MaxRetries)
                {
                    logger.LogError("Poison message {MessageId} exceeded retries. Deleting.", message.MessageId);
                    await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    continue;
                }

                // 2. Deserialization
                try
                {
                    var request = JsonSerializer.Deserialize<ViolationPayload>(message.Body);
                    if (request != null)
                    {
                        validMessages.Add(message);
                        requests.Add(request);
                    }
                    else
                    {
                        throw new Exception("Empty payload");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to deserialize message {MessageId}. Deleting.", message.MessageId);
                    await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                }
            }

            if (!requests.Any()) return;

            // 3. Bulk Processing (Single Service Call -> Single DB Transaction)
            try
            {
                using var scope = scopeFactory.CreateScope();
                var violationService = scope.ServiceProvider.GetRequiredService<IViolationService>();

                var processedCount = await violationService.ProcessViolationsBulkAsync(requests);
                
                logger.LogInformation("Successfully processed {Count} violations in bulk", processedCount);

                // 4. Batch Delete from SQS
                var deleteEntries = validMessages.Select(m => new DeleteMessageBatchRequestEntry
                {
                    Id = m.MessageId,
                    ReceiptHandle = m.ReceiptHandle
                }).ToList();

                await sqs.DeleteMessageBatchAsync(queueUrl, deleteEntries, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical failure during bulk processing. Batch will be retried by SQS.");
            }
        }
    }
}