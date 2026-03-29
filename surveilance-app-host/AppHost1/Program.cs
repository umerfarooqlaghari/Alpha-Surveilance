using Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
// ... existing postgres/audit/redis code ...
var postgres = builder.AddPostgres("violation-db-server-1")
.WithPgAdmin()
.WithDataVolume();

var violationDb_T1 = postgres.AddDatabase("violations");

var auditPostgres = builder.AddPostgres("audit-db-server-1")
    .WithImage("timescale/timescaledb", "latest-pg16") 
    .WithPgAdmin() 
    .WithDataVolume();

var auditDb_T1 = auditPostgres.AddDatabase("audit-logs");
var redis = builder.AddRedis("cache");

// ─── Environment Configuration ──────────────────────────────────────────────
// This is the SINGLE SOURCE OF TRUTH for the entire system behavior.
// - true:  AI runs locally, AWS/Cloud calls are SKIPPED, local logs enhanced.
// - false: Production mode, full AWS integration enabled.
var isTestingMode = builder.Configuration["GlobalSettings:TestingMode"]?.ToLower() == "true";

var auditApi = builder.AddProject<Projects.audit_api>("audit-api")
    .WithHttpEndpoint(port: 5003, name: "http", isProxied: false)
    .WithHttpEndpoint(port: 5203, name: "grpc", isProxied: false)
    .WithReference(auditDb_T1)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower());

var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(Amazon.RegionEndpoint.USEast1);

var sqsQueue = builder.AddAWSCloudFormationTemplate(
        "violation-queue",
        "sqs-template.json")
    .WithReference(awsConfig);

var violationManagementApi = builder.AddProject<Projects.violation_management_api>("violation-management-api")
    .WithHttpEndpoint(port: 5001, name: "http", isProxied: false)
    .WithReference(violationDb_T1)
    .WithReference(auditApi)
    .WithReference(redis)
    .WithReference(sqsQueue)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("SQSConfig:QueueUrl", sqsQueue.GetOutput("ViolationQueueUrl"));

// Add the BFF (Gateway)
var bff = builder.AddProject<Projects.alpha_surveilance_bff>("bff")
    .WithHttpEndpoint(port: 5002, name: "http", isProxied: false)
    .WithHttpEndpoint(port: 5202, name: "grpc", isProxied: false)
    .WithReference(violationManagementApi)
    .WithReference(auditApi)
    .WithReference(redis)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithExternalHttpEndpoints();

var visionInferenceService = builder.AddDockerfile("vision-inference-service", "../../vision-inference-service")
    .WithHttpEndpoint(name: "http", targetPort: 8000, env: "PORT")
    .WithReference(sqsQueue)
    .WithReference(violationManagementApi)
    .WithBindMount(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.aws", "/root/.aws", isReadOnly: true)
    // Persist HuggingFace downloaded models across container restarts
    .WithBindMount(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "alpha-surveillance", "hf-cache"), "/root/.cache/huggingface")
    // AWS
    .WithEnvironment("SQS_QUEUE_URL", sqsQueue.GetOutput("ViolationQueueUrl"))
    .WithEnvironment("S3_BUCKET_NAME", builder.Configuration["S3Config:BucketName"])
    .WithEnvironment("AWS_REGION", builder.Configuration["AWS:Region"])
    // Service-to-service auth
    .WithEnvironment("INTERNAL_API_KEY", builder.Configuration["InternalApi:ApiKey"] ?? "alpha-vision-internal")
    // Cloudflare Auth for WebRTC Ingest
    .WithEnvironment("CLOUDFLARE_API_TOKEN", builder.Configuration["Cloudflare:ApiToken"])
    // Roboflow inference API
    .WithEnvironment("ROBOFLOW_API_KEY", builder.Configuration["Roboflow:ApiKey"] ?? "dummy_key_please_replace")
    // Violation API base URL (Aspire injects the correct container-to-host or host-to-host URL)
    .WithEnvironment("VIOLATION_API_BASE_URL", violationManagementApi.GetEndpoint("http"))
    // ── Unified Testing Flag ──
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    // RTSP engine tuning
    .WithEnvironment("TARGET_FPS", builder.Configuration["VisionService:TargetFps"] ?? "1.0")
    .WithEnvironment("FRAME_TIMEOUT_SECONDS", builder.Configuration["VisionService:FrameTimeoutSeconds"] ?? "30")
    .WithEnvironment("CAMERA_POLL_INTERVAL_SECONDS", builder.Configuration["VisionService:CameraPollIntervalSeconds"] ?? "60")
    .WithEnvironment("MAX_STREAM_WORKERS", builder.Configuration["VisionService:MaxStreamWorkers"] ?? "500")
    .WithEnvironment("CLOUDINARY_CLOUD_NAME", builder.Configuration["Cloudinary:CloudName"])
    .WithEnvironment("CLOUDINARY_API_KEY", builder.Configuration["Cloudinary:ApiKey"])
    .WithEnvironment("CLOUDINARY_API_SECRET", builder.Configuration["Cloudinary:ApiSecret"]);

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithEnvironment("BFF_URL", bff.GetEndpoint("http"))
    .WithEnvironment("NEXT_PUBLIC_BFF_URL", bff.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();
    

builder.Build().Run();
