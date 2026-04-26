using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AlphaSurveilance.Services.Interfaces
{


    public interface IEmailService
    {
        Task<bool> SendEmailAsync(List<string> to, string subject, string body, List<AttachmentDto>? attachments = null);
        string ProviderName { get; }
    }
}
