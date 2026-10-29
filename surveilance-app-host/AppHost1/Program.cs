using Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// ─── 1. Infrastructure (External Render Postgres + Local Redis Cache) ─────

// External Render-hosted databases — connection strings come from appsettings.json
var violationDb = builder.AddConnectionString("violations");
var auditDb = builder.AddConnectionString("audit-logs");

var redis = builder.AddRedis("cache");
// Manually pinning the port of the AUTO-CREATED 'tcp' endpoint
redis.WithEndpoint("tcp", endpoint => { endpoint.Port = 6379; });

// ─── 2. Global Settings ─────────────────────────────────────────────────────
var isTestingMode = builder.Configuration["GlobalSettings:TestingMode"]?.ToLower() == "true";
var internalApiKey = builder.Configuration["InternalApi:ApiKey"]
    ?? builder.Configuration["InternalApiKey"]
    ?? builder.Configuration["INTERNAL_API_KEY"]
    ?? "";
var roboflowApiKey = builder.Configuration["Roboflow:ApiKey"] ?? "";

// ─── 3. Application Services (APIs) ─────────────────────────────────────────

var auditApi = builder.AddProject<Projects.audit_api>("audit-api")
    .WithReference(auditDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("InternalApi__ApiKey", internalApiKey);

// SURGICAL OVERRIDE for Audit API
auditApi.WithEndpoint("http", endpoint => { endpoint.Port = 5003; endpoint.IsProxied = false; });
auditApi.WithEndpoint("grpc", endpoint => { endpoint.Port = 5203; endpoint.IsProxied = false; });

builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(Amazon.RegionEndpoint.USEast1);

var sqsQueueUrl = builder.Configuration["SQSConfig:QueueUrl"]
    ?? throw new InvalidOperationException(
        "SQSConfig:QueueUrl is missing in AppHost configuration. Add it to appsettings.Development.json.");

var violationApi = builder.AddProject<Projects.violation_management_api>("violation-api")
    .WithReference(violationDb)
    .WithReference(auditApi)
    .WithReference(redis)
    .WaitFor(auditApi)
    .WaitFor(redis)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("InternalApi__ApiKey", internalApiKey)
    .WithEnvironment("SQSConfig__QueueUrl", sqsQueueUrl)
    .WithEnvironment("Services__AuditApi__GrpcUrl", auditApi.GetEndpoint("grpc"))
    .WithEnvironment("Services__Bff__GrpcUrl", "http://localhost:5202")
    .WithEnvironment("Services__Reid__HttpUrl", "http://localhost:8001")
    .WithEnvironment("VisionService__BaseUrl", "http://localhost:8000");

// SURGICAL OVERRIDE for Violation API
violationApi.WithEndpoint("http", endpoint => { endpoint.Port = 5001; endpoint.IsProxied = false; });

// ─── 4. Gateway & UI ────────────────────────────────────────────────────────

var modelCacheRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".alpha-surveillance",
    "model_cache");
Directory.CreateDirectory(Path.Combine(modelCacheRoot, "ultralytics"));
Directory.CreateDirectory(Path.Combine(modelCacheRoot, "torch"));
Directory.CreateDirectory(Path.Combine(modelCacheRoot, "clip"));
Directory.CreateDirectory(Path.Combine(modelCacheRoot, "models"));

var bff = builder.AddProject<Projects.alpha_surveilance_bff>("bff")
    .WithReference(violationApi)
    .WithReference(auditApi)
    .WithReference(redis)
    .WaitFor(violationApi)
    .WaitFor(auditApi)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("Services__AuditApi__GrpcUrl", auditApi.GetEndpoint("grpc"))
    .WithEnvironment("Services__ViolationApi__HttpUrl", "http://localhost:5001")
    .WithEnvironment("InternalApi__ApiKey", internalApiKey);

// SURGICAL OVERRIDE for BFF
bff.WithEndpoint("http", endpoint => { endpoint.Port = 5002; endpoint.IsProxied = false; });
bff.WithEndpoint("grpc", endpoint => { endpoint.Port = 5202; endpoint.IsProxied = false; });

var visionInference = builder.AddDockerfile("vision-inference", "../../vision-inference-service")
    .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway", "--dns", "8.8.8.8")
    .WithHttpEndpoint(name: "vision-http", port: 8000, targetPort: 8000, env: "PORT")
    .WithReference(violationApi)
    .WaitFor(violationApi)
    // Mount to /home/vision/.aws, not /root/.aws — the container runs as uid 1001 (vision user)
    // and cannot read /root/.aws, which silently breaks all AWS SDK calls (S3, SQS).
    .WithBindMount(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.aws", "/home/vision/.aws", isReadOnly: true)
    .WithBindMount(Path.Combine(modelCacheRoot, "ultralytics"), "/tmp/Ultralytics")
    .WithBindMount(Path.Combine(modelCacheRoot, "torch"), "/root/.cache/torch")
    .WithBindMount(Path.Combine(modelCacheRoot, "clip"), "/root/.cache/clip")
    .WithBindMount(Path.Combine(modelCacheRoot, "models"), "/tmp/models")
    .WithEnvironment("AWS_REGION", "us-east-1")
    .WithEnvironment("SQS_QUEUE_URL", sqsQueueUrl)
    .WithEnvironment("DEVICE_ID", builder.Configuration["EdgeDevice:DeviceId"] ?? "")
    .WithEnvironment("VIOLATION_API_BASE_URL", "http://host.docker.internal:5001")
    .WithEnvironment("INTERNAL_API_KEY", internalApiKey)
    .WithEnvironment("ROBOFLOW_API_KEY", roboflowApiKey)
    .WithEnvironment("RESTAURANT_PPE_MODEL_IDENTIFIER", "restaurant-ppe-v1")
    // NOTE: the actual cached/exported weights on disk are named without a
    // "-v2" suffix (see violation-management-api Program.cs AiModel sync,
    // which explicitly repairs any DB row still pointing at the old
    // "-v2" name). Keep this fallback aligned with that canonical name so a
    // fresh DB or a rule missing model_local_path doesn't fall back to a
    // file/S3 key that doesn't exist.
    .WithEnvironment("RESTAURANT_PPE_MODEL_PATH", "/tmp/models/restaurant-ppe-yolo11m.pt")
    .WithEnvironment("MODEL_S3_KEY", "models/restaurant-ppe-yolo11m.pt")
    .WithEnvironment("RESTAURANT_PPE_IMAGE_SIZE", "960")
    // 0.55 balances recall vs precision: 0.40 let weak detections through
    // (e.g. surgical masks flagged as incorrect-mask at 0.47), while 0.60
    // was too aggressive on dim CCTV scenes. Geofence + 3-frame hysteresis
    // still gate the remaining false positives.
    .WithEnvironment("MODEL_CACHE_DIR", "/tmp/models")
    .WithEnvironment("MIN_CONFIDENCE_RESTAURANT_PPE", "0.55")
    // CLAHE + conditional gamma low-light preprocessing. Set to "false" to A/B.
    .WithEnvironment("RESTAURANT_PPE_ENHANCE_LOWLIGHT", "true")
    // Person-crop pre-layer: detect persons first, run PPE on each padded
    // crop. Massively improves recall for mask/hairnet on wide-angle CCTV
    // because the face goes from ~70 px to ~400 px. Falls back to full-frame
    // PPE inference when no person is detected.
    .WithEnvironment("RESTAURANT_PPE_PERSON_CROP", "true")
    .WithEnvironment("PERSON_DETECTOR_CONFIDENCE", "0.20")
    .WithEnvironment("RESTAURANT_PPE_FALLBACK_FULL_FRAME_ON_NO_PERSON", "false")
    .WithEnvironment("PERSON_CROP_PADDING", "0.15")
    .WithEnvironment("RESTAURANT_PPE_PREFER_NO_MASK_LABEL", "true")
    .WithEnvironment("RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER", "false")
    .WithEnvironment("RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO", "0.90")
    .WithEnvironment("HUMAN_REID_MATCH_THRESHOLD", "0.92")
    .WithEnvironment("CONFIG_POLL_INTERVAL_SECONDS", "60")
    .WithEnvironment("S3_BUCKET_NAME", builder.Configuration["S3Config:BucketName"] ?? "alpha-surveilance-assets")
    // config.py requires MODEL_S3_BUCKET (the bucket model_loader.py downloads
    // weights from) and hard-fails at startup if it's unset outside TESTING_MODE.
    // Model weights live in alpha-surveilance-models under a "models/" prefix.
    .WithEnvironment("MODEL_S3_BUCKET", builder.Configuration["S3Config:ModelBucketName"] ?? "alpha-surveilance-models")
    .WithEnvironment("MAX_STREAM_LAG_SECONDS", "5.0")
    // Re-ID service runs in its own container on the host network at 8001.
    // Inside the vision container, `localhost` is the container itself —
    // must use host.docker.internal so requests cross the bridge to the
    // reid container's published port.
    .WithEnvironment("HUMAN_REID_URL", "http://host.docker.internal:8001")
    .WithEnvironment("TESTING_MODE", "false");

// human-reid is a Python/SQLAlchemy service, so DATABASE_URL must be a
// postgresql:// URL. The other connection strings here are .NET ADO strings
// ("Host=...;Port=...;Database=..."), and handing that form to SQLAlchemy fails
// at import with "Could not parse SQLAlchemy URL from given URL string" — the
// container starts, then dies before serving anything. Normalise here so
// ConnectionStrings:reid can be written in either spelling.
static string ToPostgresUrl(string cs)
{
    if (string.IsNullOrWhiteSpace(cs)) return cs;
    var trimmed = cs.Trim();
    if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return trimmed;
    }

    var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var part in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var eq = part.IndexOf('=');
        if (eq <= 0) continue;
        kv[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
    }

    // Not a shape we recognise — pass through untouched rather than mangling it.
    if (!kv.TryGetValue("Host", out var host) || string.IsNullOrWhiteSpace(host))
    {
        return trimmed;
    }

    var port = kv.TryGetValue("Port", out var p) && !string.IsNullOrWhiteSpace(p) ? p : "5432";
    var database = kv.TryGetValue("Database", out var d) && !string.IsNullOrWhiteSpace(d) ? d : "postgres";
    var user = Uri.EscapeDataString(kv.TryGetValue("Username", out var u) ? u : "postgres");
    var pass = Uri.EscapeDataString(kv.TryGetValue("Password", out var pw) ? pw : string.Empty);

    var url = $"postgresql://{user}:{pass}@{host}:{port}/{database}";
    if (kv.TryGetValue("SSL Mode", out var ssl) &&
        ssl.Contains("require", StringComparison.OrdinalIgnoreCase))
    {
        url += "?sslmode=require";
    }
    return url;
}

// Build context is human-reid-service directory
var reidConnectionString = builder.Configuration.GetConnectionString("reid")
    ?? throw new InvalidOperationException(
        "Connection string 'reid' is not configured. Set ConnectionStrings:reid in appsettings.development.json or via user-secrets/env.");

var reidService = builder.AddDockerfile("human-reid", "../../human-reid-service")
    .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway")
    .WithHttpEndpoint(name: "reid-http", port: 8001, targetPort: 8001, env: "PORT")
    .WithEnvironment("DATABASE_URL", ToPostgresUrl(reidConnectionString));

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithEnvironment("NEXT_PUBLIC_BFF_URL", "http://localhost:5002")
    .WithHttpEndpoint(port: 3000, env: "PORT", isProxied: false);

builder.Build().Run();
