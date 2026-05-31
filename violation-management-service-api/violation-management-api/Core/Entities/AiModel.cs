namespace violation_management_api.Core.Entities;

/// <summary>
/// Registry of every AI model used by the Vision Inference Service.
/// SuperAdmin manages entries here; the inference service resolves
/// a SopViolationType.ModelIdentifier string to the matching ModelKey
/// and picks up the download URL / local path / status from this record.
/// </summary>
public class AiModel
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique slug that maps 1-to-1 with SopViolationType.ModelIdentifier.
    /// e.g. "restaurant-ppe-v1", "pest-detection-v1", "human-detection-v1"
    /// </summary>
    public string ModelKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public AiModelType ModelType { get; set; } = AiModelType.YoloLocal;
    public AiModelStatus Status { get; set; } = AiModelStatus.Registered;

    // ── Where to get the weights file ────────────────────────────────────────
    /// <summary>Direct HTTPS or presigned S3 URL the edge device downloads from.</summary>
    public string? DownloadUrl { get; set; }
    public string? S3Bucket { get; set; }
    public string? S3Key { get; set; }

    // ── Where it lives on the edge device after download ─────────────────────
    /// <summary>Absolute path expected on the edge device, e.g. /tmp/models/restaurant-ppe-v2.pt</summary>
    public string? LocalPath { get; set; }

    // ── Integrity ─────────────────────────────────────────────────────────────
    public string? Version { get; set; }
    public long?   FileSizeBytes { get; set; }
    public string? Sha256Checksum { get; set; }

    // ── Runtime state (written back by edge device callbacks) ─────────────────
    public string?   ErrorMessage { get; set; }
    public DateTime? DownloadedAt { get; set; }

    // ── Timestamps / soft delete ──────────────────────────────────────────────
    public DateTime  CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<SopViolationType> SopViolationTypes { get; set; } = new List<SopViolationType>();
}

public enum AiModelType
{
    /// <summary>YOLO .pt file fetched from URL or S3 and run locally on the edge device.</summary>
    YoloLocal     = 0,

    /// <summary>Inference delegated to the Roboflow hosted API; no file download needed.</summary>
    RoboflowCloud = 1,

    /// <summary>Custom fine-tuned YOLO .pt (same loading path as YoloLocal, different provenance).</summary>
    YoloFineTuned = 2,

    /// <summary>
    /// Open-vocabulary grounding model driven by per-rule trigger labels.
    /// Intended for Locate-Anything style experimental detectors.
    /// </summary>
    OpenVocabGrounding = 3,
}

public enum AiModelStatus
{
    /// <summary>Metadata registered but file not yet on any edge device.</summary>
    Registered  = 0,

    /// <summary>Edge device is currently downloading the file (transient).</summary>
    Downloading = 1,

    /// <summary>File confirmed present and ready for inference.</summary>
    Available   = 2,

    /// <summary>SuperAdmin has disabled this model — inference skips all rules that reference it.</summary>
    Disabled    = 3,

    /// <summary>Last download attempt failed; ErrorMessage contains details.</summary>
    Error       = 4,
}
