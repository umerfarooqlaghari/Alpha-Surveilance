namespace alpha_surveilance_bff.Hubs
{
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.AspNetCore.Authorization;
    using System.Threading.Tasks;

    [Microsoft.AspNetCore.Authorization.Authorize] 
    public class ViolationHub : Hub
    {
        // Users (UI Clients) join their specific "Tenant Group" automatically based on their JWT.
        // This prevents users from "spying" on other tenants by guessing IDs.
        public async Task JoinTenantGroup()
        {
            var tenantId = Context.User?.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId))
            {
                throw new HubException("Tenant ID not found in security context.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);
        }

        public async Task LeaveTenantGroup()
        {
            var tenantId = Context.User?.FindFirst("tenantId")?.Value;
            if (!string.IsNullOrEmpty(tenantId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenantId);
            }
        }
    }
}
