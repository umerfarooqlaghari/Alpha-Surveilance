using audit_api.Core.Domain;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace audit_api.Data.Repositories.Interfaces
{
    public interface IAuditRepository
    {
        Task AddLogAsync(AuditLog log);
        Task<IEnumerable<AuditLog>> GetLogsByTenantAsync(string tenantId);
        Task<IEnumerable<AuditLog>> GetLogsByViolationIdAsync(Guid violationId);
        Task SaveChangesAsync();
    }
}
