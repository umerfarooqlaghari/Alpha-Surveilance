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
    ?? throw new InvalidOperationException("InternalApi:ApiKey is missing in AppHost configuration. Please add it to appsettings.json.");
var roboflowApiKey = builder.Configuration["Roboflow:ApiKey"]
    ?? throw new InvalidOperationException("Roboflow:ApiKey is missing in AppHost configuration.");

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

var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(Amazon.RegionEndpoint.USEast1);

var sqsQueue = builder.AddAWSCloudFormationTemplate(
    "violation-queue",
    "sqs-template.json")
    .WithReference(awsConfig);

var violationApi = builder.AddProject<Projects.violation_management_api>("violation-api")
    .WithReference(violationDb)
    .WithReference(auditApi)
    .WithReference(redis)
    .WithReference(sqsQueue)
    .WaitFor(auditApi)
    .WaitFor(redis)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("InternalApi__ApiKey", internalApiKey)
    .WithEnvironment("SQSConfig__QueueUrl", sqsQueue.GetOutput("ViolationQueueUrl"))
    .WithEnvironment("Services__AuditApi__GrpcUrl", auditApi.GetEndpoint("grpc"))
    .WithEnvironment("Services__Bff__GrpcUrl", "http://localhost:5202")
    .WithEnvironment("Services__Reid__HttpUrl", "http://localhost:8001");

// SURGICAL OVERRIDE for Violation API
violationApi.WithEndpoint("http", endpoint => { endpoint.Port = 5001; endpoint.IsProxied = false; });

// ─── 4. Gateway & UI ────────────────────────────────────────────────────────

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
    .WithHttpEndpoint(name: "vision-http", port: 8000, targetPort: 8000, env: "PORT")
    .WithReference(sqsQueue)
    .WithReference(violationApi)
    .WaitFor(violationApi)
    .WithBindMount(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.aws", "/root/.aws", isReadOnly: true)
    .WithBindMount("./.model_cache/ultralytics", "/tmp/Ultralytics")
    .WithBindMount("./.model_cache/torch", "/root/.cache/torch")
    .WithBindMount("./.model_cache/clip", "/root/.cache/clip")
    .WithBindMount("./.model_cache/models", "/tmp/models")
    .WithEnvironment("SQS_QUEUE_URL", sqsQueue.GetOutput("ViolationQueueUrl"))
    .WithEnvironment("VIOLATION_API_BASE_URL", "http://host.docker.internal:5001")
    .WithEnvironment("INTERNAL_API_KEY", internalApiKey)
    .WithEnvironment("ROBOFLOW_API_KEY", roboflowApiKey)
    .WithEnvironment("S3_BUCKET_NAME", builder.Configuration["S3Config:BucketName"] ?? "alphasurveilance-dev-1")
    .WithEnvironment("MAX_STREAM_LAG_SECONDS", "5.0")
    .WithEnvironment("TESTING_MODE", "false");

var reidService = builder.AddDockerfile("human-reid", "../../human-reid-service")
    .WithHttpEndpoint(name: "reid-http", port: 8001, targetPort: 8001, env: "PORT")
    .WithEnvironment("DATABASE_URL",
        builder.Configuration.GetConnectionString("reid")
            ?? throw new InvalidOperationException(
                "Connection string 'reid' is not configured. Set ConnectionStrings:reid in appsettings.development.json or via user-secrets/env."));

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithEnvironment("NEXT_PUBLIC_BFF_URL", "http://localhost:5002")
    .WithHttpEndpoint(port: 3000, env: "PORT", isProxied: false);

builder.Build().Run();
