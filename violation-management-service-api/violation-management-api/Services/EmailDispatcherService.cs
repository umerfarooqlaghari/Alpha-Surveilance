using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.Services
{
    public class EmailDispatcherService(
        IEnumerable<IEmailService> emailServices,
        IConfiguration config,
        ILogger<EmailDispatcherService> logger)
    {
        public async Task<bool> SendEmailAsync(List<string> to, string subject, string body, List<AttachmentDto>? attachments = null)
        {
            var preferredProvider = config["Email:PreferredProvider"] ?? "Brevo";
            
            var provider = emailServices.FirstOrDefault(s => s.ProviderName == preferredProvider) 
                           ?? emailServices.First();

            logger.LogInformation("Dispatching email using provider: {Provider}", provider.ProviderName);
            
            var success = await provider.SendEmailAsync(to, subject, body, attachments);

            // Fallback logic if primary fails
            if (!success)
            {
                var fallback = emailServices.FirstOrDefault(s => s.ProviderName != provider.ProviderName);
                if (fallback != null)
                {
                    logger.LogWarning("Primary email provider {Primary} failed. Attempting fallback to {Fallback}", 
                        provider.ProviderName, fallback.ProviderName);
                    return await fallback.SendEmailAsync(to, subject, body, attachments);
                }
            }

            return success;
        }
    }
}
