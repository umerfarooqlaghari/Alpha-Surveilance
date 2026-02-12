using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AlphaSurveilance.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.Services
{
    public class BrevoEmailService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<BrevoEmailService> logger) : IEmailService
    {
        public string ProviderName => "Brevo";

        public async Task<bool> SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var apiKey = config["Email:BrevoApiKey"];
                var senderEmail = config["Email:SenderEmail"];
                var senderName = config["Email:SenderName"];

                var payload = new
                {
                    sender = new { email = senderEmail, name = senderName },
                    to = new[] { new { email = to } },
                    subject = subject,
                    htmlContent = body
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Email sent successfully via Brevo to {To}", to);
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();
                logger.LogError("Failed to send email via Brevo. Status: {Status}, Error: {Error}", response.StatusCode, error);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception while sending email via Brevo");
                return false;
            }
        }
    }
}
