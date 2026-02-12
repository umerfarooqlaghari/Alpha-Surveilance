using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlphaSurveilance.Services.Interfaces;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.Services
{
    public class SesEmailService(
        IAmazonSimpleEmailService ses,
        IConfiguration config,
        ILogger<SesEmailService> logger) : IEmailService
    {
        public string ProviderName => "AWS SES";

        public async Task<bool> SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var senderEmail = config["Email:SenderEmail"];

                var request = new SendEmailRequest
                {
                    Source = senderEmail,
                    Destination = new Destination
                    {
                        ToAddresses = new List<string> { to }
                    },
                    Message = new Message
                    {
                        Subject = new Content(subject),
                        Body = new Body
                        {
                            Html = new Content(body)
                        }
                    }
                };

                var response = await ses.SendEmailAsync(request);
                
                logger.LogInformation("Email sent successfully via SES to {To} with MessageId {MessageId}", 
                    to, response.MessageId);
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email via SES");
                return false;
            }
        }
    }
}
