using System;
using AlphaSurveilance.Core.Domain;
using Pgvector;

namespace violation_management_api.Core.Entities;

/// <summary>
/// Worker facial / body Re-ID profile per tenant with 512-dimensional vector embedding.
/// Edge devices query this central table to consistently identify workers across multiple cameras
/// and edge nodes at a tenant's facility.
/// </summary>
public class WorkerProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Human-friendly worker or role identifier (e.g. "Worker_01", "Reliever_A", "Chef_Mario").
    /// </summary>
    public string PersonTag { get; set; } = string.Empty;

    /// <summary>
    /// 512-dimensional normalized feature embedding extracted by edge Re-ID model.
    /// </summary>
    public Vector Embedding { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
