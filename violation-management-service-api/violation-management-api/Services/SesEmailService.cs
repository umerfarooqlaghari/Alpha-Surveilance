using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AlphaSurveilance.Services.Interfaces;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AlphaSurveilance.Services
{
    public class SesEmailService(
        IAmazonSimpleEmailService ses,
        ILogger<SesEmailService> logger) : IEmailService
    {
        public string ProviderName => "AWS SES";

        public async Task<bool> SendEmailAsync(List<string> to, string subject, string body, List<AttachmentDto>? attachments = null)
        {
            try
            {
                var senderEmail = "info@alpha-devs.cloud"; // Hardcoded as per requirement, or config["Email:SenderEmail"]

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Alpha Surveillance", senderEmail));
                
                foreach (var recipient in to)
                {
                    message.To.Add(new MailboxAddress("", recipient));
                }

                message.Subject = subject;

                var builder = new BodyBuilder
                {
                    HtmlBody = body
                };

                if (attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        builder.Attachments.Add(attachment.FileName, attachment.Content, MimeKit.ContentType.Parse(attachment.ContentType));
                    }
                }

                message.Body = builder.ToMessageBody();

                using var memoryStream = new MemoryStream();
                await message.WriteToAsync(memoryStream);

                var sendRequest = new SendRawEmailRequest
                {
                    RawMessage = new RawMessage(memoryStream)
                };

                var response = await ses.SendRawEmailAsync(sendRequest);
                
                logger.LogInformation("Email sent successfully via SES to {Count} recipients with MessageId {MessageId}", 
                    to.Count, response.MessageId);
                
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
