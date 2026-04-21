using Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// ─── 1. Infrastructure (Databases & Cache) ──────────────────────────────────

var postgres = builder.AddPostgres("violation-db-v16")
    .WithImage("postgres", "16-alpine")
    .WithPgAdmin()
    .WithDataVolume();
// Manually pinning the port of the AUTO-CREATED 'tcp' endpoint 
postgres.WithEndpoint("tcp", endpoint => { endpoint.Port = 5432; });

var violationDb = postgres.AddDatabase("violations");

var auditPostgres = builder.AddPostgres("audit-db-v16")
    .WithImage("timescale/timescaledb", "latest-pg16") 
    .WithPgAdmin() 
    .WithDataVolume();
// Manually pinning the port of the AUTO-CREATED 'tcp' endpoint
auditPostgres.WithEndpoint("tcp", endpoint => { endpoint.Port = 5433; });

var auditDb = auditPostgres.AddDatabase("audit-logs");

var redis = builder.AddRedis("cache");
// Manually pinning the port of the AUTO-CREATED 'tcp' endpoint
redis.WithEndpoint("tcp", endpoint => { endpoint.Port = 6379; });

// ─── 2. Global Settings ─────────────────────────────────────────────────────
var isTestingMode = builder.Configuration["GlobalSettings:TestingMode"]?.ToLower() == "true";

// ─── 3. Application Services (APIs) ─────────────────────────────────────────

var auditApi = builder.AddProject<Projects.audit_api>("audit-api")
    .WithReference(auditDb)
    .WithReference(redis)
    .WaitFor(auditDb)
    .WaitFor(redis)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower());

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
    .WaitFor(violationDb)
    .WaitFor(auditApi)
    .WaitFor(redis)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("SQSConfig__QueueUrl", sqsQueue.GetOutput("ViolationQueueUrl"))
    .WithEnvironment("Services__AuditApi__GrpcUrl", auditApi.GetEndpoint("grpc"))
    .WithEnvironment("Services__Bff__GrpcUrl", "http://localhost:5202");

// SURGICAL OVERRIDE for Violation API
violationApi.WithEndpoint("http", endpoint => { endpoint.Port = 5001; endpoint.IsProxied = false; });

// ─── 4. Gateway & UI ────────────────────────────────────────────────────────

var bff = builder.AddProject<Projects.alpha_surveilance_bff>("bff")
    .WithReference(violationApi)
    .WithReference(auditApi)
    .WithReference(redis)
    .WaitFor(violationApi)
    .WaitFor(auditApi)
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower())
    .WithEnvironment("Services__AuditApi__GrpcUrl", auditApi.GetEndpoint("grpc"))
    .WithEnvironment("Services__ViolationApi__HttpUrl", "http://localhost:5001");

// SURGICAL OVERRIDE for BFF
bff.WithEndpoint("http", endpoint => { endpoint.Port = 5002; endpoint.IsProxied = false; });
bff.WithEndpoint("grpc", endpoint => { endpoint.Port = 5202; endpoint.IsProxied = false; });

var visionInference = builder.AddDockerfile("vision-inference", "../../vision-inference-service")
    .WithHttpEndpoint(name: "vision-http", targetPort: 8000, env: "PORT")
    .WithReference(sqsQueue)
    .WithReference(violationApi)
    .WaitFor(violationApi)
    .WithBindMount(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.aws", "/root/.aws", isReadOnly: true)
    .WithEnvironment("SQS_QUEUE_URL", sqsQueue.GetOutput("ViolationQueueUrl"))
    .WithEnvironment("VIOLATION_API_BASE_URL", "http://localhost:5001")
    .WithEnvironment("TESTING_MODE", isTestingMode.ToString().ToLower());

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithEnvironment("NEXT_PUBLIC_BFF_URL", "http://localhost:5002")
    .WithHttpEndpoint(port: 3000, env: "PORT", isProxied: false);

builder.Build().Run();
