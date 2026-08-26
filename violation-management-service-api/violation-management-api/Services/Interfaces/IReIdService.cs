using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using violation_management_api.DTOs.Responses;

namespace violation_management_api.Services.Interfaces;

public interface IReIdService
{
    /// <summary>
    /// Identify an unknown 512-dim Re-ID embedding against the tenant's central worker profiles using pgvector cosine similarity.
    /// </summary>
    Task<IdentifyResponse> IdentifyAsync(Guid tenantId, List<float> embedding, float threshold);

    /// <summary>
    /// Upsert or enroll a worker Re-ID profile for a tenant.
    /// </summary>
    Task<Guid> EnrollWorkerProfileAsync(Guid tenantId, string personTag, List<float> embedding);
}
