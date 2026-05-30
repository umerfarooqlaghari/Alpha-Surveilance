using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class AiModelService : IAiModelService
{
    private readonly AppViolationDbContext _db;
    private readonly ILogger<AiModelService> _logger;

    public AiModelService(AppViolationDbContext db, ILogger<AiModelService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<AiModelResponse>> GetAllAsync()
    {
        var models = await _db.AiModels
            .AsNoTracking()
            .Select(m => new
            {
                Model = m,
                SopRuleCount = m.SopViolationTypes.Count(s => !s.IsDeleted)
            })
            .OrderBy(x => x.Model.DisplayName)
            .ToListAsync();

        return models.Select(x => ToResponse(x.Model, x.SopRuleCount)).ToList();
    }

    public async Task<AiModelResponse?> GetByIdAsync(Guid id)
    {
        var row = await _db.AiModels
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                Model = m,
                SopRuleCount = m.SopViolationTypes.Count(s => !s.IsDeleted)
            })
            .FirstOrDefaultAsync();

        return row is null ? null : ToResponse(row.Model, row.SopRuleCount);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<AiModelResponse> RegisterAsync(RegisterAiModelRequest request)
    {
        var normalized = NormalizeRequest(request);

        var duplicate = await _db.AiModels.AnyAsync(m =>
            m.ModelKey.ToLower() == normalized.ModelKey.ToLower());
        if (duplicate)
            throw new InvalidOperationException($"A model with key '{normalized.ModelKey}' already exists.");

        var model = new AiModel
        {
            Id           = Guid.NewGuid(),
            ModelKey     = normalized.ModelKey,
            DisplayName  = normalized.DisplayName,
            Description  = normalized.Description,
            ModelType    = normalized.ModelType,
            Status       = AiModelStatus.Registered,
            DownloadUrl  = normalized.DownloadUrl,
            S3Bucket     = normalized.S3Bucket,
            S3Key        = normalized.S3Key,
            LocalPath    = normalized.LocalPath,
            Version      = normalized.Version,
            Sha256Checksum = normalized.Sha256Checksum,
            CreatedAt    = DateTime.UtcNow,
        };

        _db.AiModels.Add(model);
        await _db.SaveChangesAsync();

        _logger.LogInformation("AiModel '{ModelKey}' registered (Id={Id})", model.ModelKey, model.Id);
        return ToResponse(model, 0);
    }

    public async Task<AiModelResponse?> UpdateAsync(Guid id, RegisterAiModelRequest request)
    {
        var normalized = NormalizeRequest(request);

        var model = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return null;

        // Guard: ModelKey change only allowed if not already used by another record
        if (!string.Equals(model.ModelKey, normalized.ModelKey, StringComparison.OrdinalIgnoreCase))
        {
            var clash = await _db.AiModels.AnyAsync(m =>
                m.Id != id &&
                m.ModelKey.ToLower() == normalized.ModelKey.ToLower());
            if (clash)
                throw new InvalidOperationException($"A model with key '{normalized.ModelKey}' already exists.");
        }

        model.ModelKey      = normalized.ModelKey;
        model.DisplayName   = normalized.DisplayName;
        model.Description   = normalized.Description;
        model.ModelType     = normalized.ModelType;
        model.DownloadUrl   = normalized.DownloadUrl;
        model.S3Bucket      = normalized.S3Bucket;
        model.S3Key         = normalized.S3Key;
        model.LocalPath     = normalized.LocalPath;
        model.Version       = normalized.Version;
        model.Sha256Checksum = normalized.Sha256Checksum;
        model.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var sopCount = await _db.SopViolationTypes.CountAsync(s => s.AiModelId == id && !s.IsDeleted);
        return ToResponse(model, sopCount);
    }

    public async Task<bool> EnableAsync(Guid id)
    {
        var model = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return false;

        model.Status    = AiModelStatus.Available;
        model.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DisableAsync(Guid id)
    {
        var model = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return false;

        model.Status    = AiModelStatus.Disabled;
        model.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id)
    {
        var model = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return (false, null);

        var refCount = await _db.SopViolationTypes.CountAsync(s => s.AiModelId == id && !s.IsDeleted);
        if (refCount > 0)
            return (false, $"Cannot delete: {refCount} SOP rule(s) still reference this model. Reassign them first.");

        model.IsDeleted = true;
        model.DeletedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Called by the edge device after a download attempt to report its outcome.
    /// </summary>
    public async Task<bool> UpdateEdgeStatusAsync(Guid id, EdgeModelStatusUpdate update)
    {
        var model = await _db.AiModels.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return false;

        if (!Enum.TryParse<AiModelStatus>(update.Status, ignoreCase: true, out var parsedStatus))
            throw new ArgumentException(
                "Invalid status. Allowed values: Registered, Downloading, Available, Disabled, Error.");

        model.Status = parsedStatus;

        if (!string.IsNullOrWhiteSpace(update.ErrorMessage))
            model.ErrorMessage = update.ErrorMessage;

        if (!string.IsNullOrWhiteSpace(update.Sha256Checksum))
            model.Sha256Checksum = update.Sha256Checksum;

        if (update.FileSizeBytes.HasValue)
            model.FileSizeBytes = update.FileSizeBytes;

        if (model.Status == AiModelStatus.Available)
            model.DownloadedAt = DateTime.UtcNow;

        model.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static AiModelResponse ToResponse(AiModel m, int sopRuleCount) => new()
    {
        Id             = m.Id,
        ModelKey       = m.ModelKey,
        DisplayName    = m.DisplayName,
        Description    = m.Description,
        ModelType      = m.ModelType.ToString(),
        Status         = m.Status.ToString(),
        DownloadUrl    = m.DownloadUrl,
        S3Bucket       = m.S3Bucket,
        S3Key          = m.S3Key,
        LocalPath      = m.LocalPath,
        Version        = m.Version,
        FileSizeBytes  = m.FileSizeBytes,
        Sha256Checksum = m.Sha256Checksum,
        ErrorMessage   = m.ErrorMessage,
        DownloadedAt   = m.DownloadedAt,
        SopRuleCount   = sopRuleCount,
        CreatedAt      = m.CreatedAt,
        UpdatedAt      = m.UpdatedAt,
    };

    private static RegisterAiModelRequest NormalizeRequest(RegisterAiModelRequest request)
    {
        if (request is null)
            throw new InvalidOperationException("Request body is required.");

        var modelKey = request.ModelKey?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(modelKey))
            throw new InvalidOperationException("ModelKey is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("DisplayName is required.");

        return new RegisterAiModelRequest
        {
            ModelKey = modelKey,
            DisplayName = displayName,
            Description = request.Description?.Trim() ?? string.Empty,
            ModelType = request.ModelType,
            DownloadUrl = request.DownloadUrl?.Trim(),
            S3Bucket = request.S3Bucket?.Trim(),
            S3Key = request.S3Key?.Trim(),
            LocalPath = request.LocalPath?.Trim(),
            Version = request.Version?.Trim(),
            Sha256Checksum = request.Sha256Checksum?.Trim(),
        };
    }
}
