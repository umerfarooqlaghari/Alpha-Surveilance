using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace alpha_surveilance_bff.Hubs
{
    // The ViolationHub is the "Radio Tower" for our frontend.
    // When a new violation occurs, the BFF will broadcast it through this Hub.
    public class ViolationHub : Hub
    {
        // Users (UI Clients) can join a "Tenant Group" to only receive alerts for their company.
        // This is a key part of our "Military Grade" tenant isolation.
        public async Task JoinTenantGroup(string tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);
        }

        public async Task LeaveTenantGroup(string tenantId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenantId);
        }
    }
}
