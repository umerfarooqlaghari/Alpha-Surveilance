using System.Threading.Tasks;
using AlphaSurveilance.DTOs.Requests;

namespace AlphaSurveilance.Services.Interfaces
{
    public interface ISqsQueueService
    {
        Task<bool> PublishViolationAsync(ViolationRequest violation);
    }
}
