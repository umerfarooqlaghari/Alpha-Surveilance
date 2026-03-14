using System;
using System.Security.Claims;
using AlphaSurveilance.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace AlphaSurveilance.Services
{
    public class CurrentTenantService : ICurrentTenantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? TenantId
        {
            get
            {
                var context = _httpContextAccessor.HttpContext;
                if (context == null) return null;

                var user = context.User;
                var isSuperAdmin = user?.IsInRole("SuperAdmin") ?? false;
                var tenantIdClaim = user?.FindFirst("tenantId")?.Value;

                // 1. If it's a regular tenant user, STRICTLY enforce the JWT claim.
                // This prevents "Header Spoofing" where a user sends a different X-Tenant-Id.
                if (!isSuperAdmin && !string.IsNullOrEmpty(tenantIdClaim))
                {
                    if (Guid.TryParse(tenantIdClaim, out var claimTenantId))
                    {
                        return claimTenantId;
                    }
                }

                // 2. If it's a SuperAdmin OR an internal headless service call (no JWT),
                // we trust the X-Tenant-Id header.
                if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenantId))
                {
                    var firstId = headerTenantId.ToString().Split(',')[0].Trim();
                    if (Guid.TryParse(firstId, out var tenantGuid))
                    {
                        return tenantGuid;
                    }
                }

                return null;
            }
        }

        public bool IsSuperAdmin
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                return user?.IsInRole("SuperAdmin") ?? false;
            }
        }
    }
}
