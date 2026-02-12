using System.Threading.Tasks;

namespace AlphaSurveilance.Services.Interfaces
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body);
        string ProviderName { get; }
    }
}
