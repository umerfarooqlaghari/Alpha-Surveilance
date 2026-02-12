using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AlphaSurveilance
{
    public interface IAuditApiClient
    {
        Task<bool> LogViolationAsync(Guid violationId, string tenantId, string type, CancellationToken token);
    }
}