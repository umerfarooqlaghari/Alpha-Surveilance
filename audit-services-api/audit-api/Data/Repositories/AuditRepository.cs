using Microsoft.EntityFrameworkCore;
using audit_api.Core.Domain;
using audit_api.Data.Repositories.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace audit_api.Data.Repositories
{
    // The AuditRepository handles persistence for system events.
    // It is injected into the gRPC service to save incoming logs.
    public class AuditRepository(AuditDbContext context) : IAuditRepository
    {
        public async Task AddLogAsync(AuditLog log)
        {
            await context.AuditLogs.AddAsync(log);
        }

        public async Task<IEnumerable<AuditLog>> GetLogsByTenantAsync(string tenantId)
        {
            // Efficient querying by TenantId and Timestamp
            return await context.AuditLogs
                .Where(l => l.TenantId == tenantId)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();
        }

        public async Task<IEnumerable<AuditLog>> GetLogsByViolationIdAsync(Guid violationId)
        {
            // TimescaleDB allows efficient querying on non-partition keys too if indexed
            return await context.AuditLogs
                .Where(l => l.ViolationId == violationId)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await context.SaveChangesAsync();
        }
    }
}
