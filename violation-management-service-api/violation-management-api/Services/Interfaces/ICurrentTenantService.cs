using System;

namespace AlphaSurveilance.Services.Interfaces
{
    public interface ICurrentTenantService
    {
        Guid? TenantId { get; }
        bool IsSuperAdmin { get; }
    }
}
