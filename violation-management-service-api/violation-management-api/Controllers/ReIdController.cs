using System;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/v1/reid")]
public class ReIdController(
    IReIdService reIdService,
    AppViolationDbContext dbContext,
    IConfiguration configuration,
    ILogger<ReIdController> logger) : ControllerBase
{
    private const int ExpectedEmbeddingDimension = 512;

    /// <summary>
    /// Authenticates an edge device via X-Device-Id and X-Device-Key headers and matches
    /// a 512-dimensional Re-ID embedding against the tenant's central worker profiles using pgvector cosine similarity.
    /// </summary>
    [HttpPost("identify")]
    public async Task<IActionResult> Identify(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        [FromHeader(Name = "X-Device-Key")] string? deviceKey,
        [FromBody] IdentifyRequest request)
    {
        // 1. Validate required security headers
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceKey))
        {
            logger.LogWarning("[ReID Identify] Rejected request: Missing X-Device-Id or X-Device-Key header.");
            return Unauthorized(new { message = "Missing X-Device-Id or X-Device-Key header." });
        }

        // 2. Look up and authenticate the edge device
        Guid? parsedGuid = Guid.TryParse(deviceId, out var g) ? g : null;
        var device = await dbContext.EdgeDevices
            .Where(d => !d.IsDeleted)
            .Where(d => (parsedGuid.HasValue && d.Id == parsedGuid.Value) || d.DeviceIdentifier == deviceId)
            .FirstOrDefaultAsync();

        if (device == null)
        {
            logger.LogWarning("[ReID Identify] Rejected request: Device '{DeviceId}' not found.", deviceId);
            return Unauthorized(new { message = "Invalid device credentials." });
        }

        if (device.Status != EdgeDeviceStatus.Active)
        {
            logger.LogWarning("[ReID Identify] Rejected request: Device '{DeviceId}' is disabled.", deviceId);
            return Unauthorized(new { message = "Device is disabled." });
        }

        // 3. Verify device secret key (device-specific key or system internal API key fallback)
        var systemInternalKey = configuration["InternalApiKey"] ?? "alpha-vision-internal";
        bool isKeyValid = false;

        if (!string.IsNullOrEmpty(device.DeviceKey) && string.Equals(device.DeviceKey, deviceKey, StringComparison.Ordinal))
        {
            isKeyValid = true;
        }
        else if (string.Equals(systemInternalKey, deviceKey, StringComparison.Ordinal))
        {
            isKeyValid = true;
        }

        if (!isKeyValid)
        {
            logger.LogWarning("[ReID Identify] Rejected request: Invalid key for device '{DeviceId}'.", deviceId);
            return Unauthorized(new { message = "Invalid device credentials." });
        }

        // 4. Validate request vector
        if (request == null || request.Embedding == null || request.Embedding.Count != ExpectedEmbeddingDimension)
        {
            return BadRequest(new
            {
                message = $"Embedding must be an array of exactly {ExpectedEmbeddingDimension} float numbers. Provided: {request?.Embedding?.Count ?? 0}"
            });
        }

        if (request.Embedding.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
        {
            return BadRequest(new { message = "Embedding contains invalid float values (NaN or Infinity)." });
        }

        float threshold = request.Threshold ?? 0.80f;

        // 5. Update device heartbeat
        try
        {
            await dbContext.EdgeDevices
                .Where(d => d.Id == device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, DateTime.UtcNow));
        }
        catch (InvalidOperationException)
        {
            device.LastSeenAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        // 6. Execute pgvector search strictly isolated to this device's tenant
        var result = await reIdService.IdentifyAsync(device.TenantId, request.Embedding, threshold);
        return Ok(result);
    }
}
