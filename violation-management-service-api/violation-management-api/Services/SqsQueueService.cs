using System;
using System.Text.Json;
using System.Threading.Tasks;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.Services.Interfaces;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.Services
{
    public class SqsQueueService(
        IAmazonSQS sqs,
        IConfiguration config,
        ILogger<SqsQueueService> logger) : ISqsQueueService
    {
        public async Task<bool> PublishViolationAsync(ViolationRequest violation)
        {
            try
            {
                var queueUrl = config.GetValue<string>("Sqs:ViolationQueueUrl");
                var messageBody = JsonSerializer.Serialize(violation);

                var request = new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = messageBody
                };

                var response = await sqs.SendMessageAsync(request);
                
                logger.LogInformation("Successfully published violation {CorrelationId} to SQS with MessageId {MessageId}", 
                    violation.CorrelationId, response.MessageId);
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish violation to SQS");
                return false;
            }
        }
    }
}
