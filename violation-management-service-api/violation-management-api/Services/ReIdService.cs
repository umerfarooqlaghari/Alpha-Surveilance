using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class ReIdService(
    AppViolationDbContext dbContext,
    ILogger<ReIdService> logger) : IReIdService
{
    private const int ExpectedEmbeddingDimension = 512;

    public async Task<IdentifyResponse> IdentifyAsync(Guid tenantId, List<float> embedding, float threshold)
    {
        if (embedding == null || embedding.Count != ExpectedEmbeddingDimension)
        {
            throw new ArgumentException(
                $"Embedding must contain exactly {ExpectedEmbeddingDimension} float values. Provided: {embedding?.Count ?? 0}",
                nameof(embedding));
        }

        var queryVector = new Vector(embedding.ToArray());

        // In relational database (PostgreSQL with pgvector), EF Core translates CosineDistance to <=>
        if (dbContext.Database.IsRelational())
        {
            var match = await dbContext.WorkerProfiles
                .Where(w => w.TenantId == tenantId)
                .OrderBy(w => w.Embedding.CosineDistance(queryVector))
                .Select(w => new
                {
                    w.PersonTag,
                    Distance = (float)w.Embedding.CosineDistance(queryVector)
                })
                .FirstOrDefaultAsync();

            if (match == null)
            {
                logger.LogDebug("[ReIdService] Tenant {TenantId} has no worker profiles enrolled.", tenantId);
                return new IdentifyResponse
                {
                    Matched = false,
                    PersonTag = null,
                    Similarity = 0.0f
                };
            }

            // Cosine similarity = 1 - Cosine distance
            float similarity = (float)Math.Round(1.0f - match.Distance, 3);
            bool isMatched = similarity >= threshold;

            logger.LogDebug(
                "[ReIdService] Tenant {TenantId} best match: '{Tag}', similarity={Sim} (threshold={Thresh}) => Matched={Matched}",
                tenantId, match.PersonTag, similarity, threshold, isMatched);

            return new IdentifyResponse
            {
                Matched = isMatched,
                PersonTag = isMatched ? match.PersonTag : null,
                Similarity = similarity
            };
        }
        else
        {
            // In-Memory test fallback
            var profiles = await dbContext.WorkerProfiles
                .Where(w => w.TenantId == tenantId)
                .ToListAsync();

            if (profiles.Count == 0)
            {
                return new IdentifyResponse
                {
                    Matched = false,
                    PersonTag = null,
                    Similarity = 0.0f
                };
            }

            var queryFloats = embedding.ToArray();
            var best = profiles
                .Select(p => new
                {
                    p.PersonTag,
                    Similarity = CalculateCosineSimilarity(queryFloats, p.Embedding.ToArray())
                })
                .OrderByDescending(p => p.Similarity)
                .FirstOrDefault();

            if (best == null)
            {
                return new IdentifyResponse { Matched = false, PersonTag = null, Similarity = 0.0f };
            }

            float sim = (float)Math.Round(best.Similarity, 3);
            bool matched = sim >= threshold;

            return new IdentifyResponse
            {
                Matched = matched,
                PersonTag = matched ? best.PersonTag : null,
                Similarity = sim
            };
        }
    }

    public async Task<Guid> EnrollWorkerProfileAsync(Guid tenantId, string personTag, List<float> embedding)
    {
        if (string.IsNullOrWhiteSpace(personTag))
        {
            throw new ArgumentException("Person tag cannot be null or empty.", nameof(personTag));
        }

        if (embedding == null || embedding.Count != ExpectedEmbeddingDimension)
        {
            throw new ArgumentException(
                $"Embedding must contain exactly {ExpectedEmbeddingDimension} float values. Provided: {embedding?.Count ?? 0}",
                nameof(embedding));
        }

        var vector = new Vector(embedding.ToArray());
        var existing = await dbContext.WorkerProfiles
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.PersonTag == personTag);

        if (existing != null)
        {
            existing.Embedding = vector;
            existing.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
            return existing.Id;
        }

        var profile = new WorkerProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PersonTag = personTag,
            Embedding = vector,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.WorkerProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        return profile.Id;
    }

    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0f;
        float normA = 0f;
        float normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA <= 0f || normB <= 0f) return 0f;
        return dot / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
