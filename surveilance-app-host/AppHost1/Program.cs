using Aspire.Hosting;
using System.Security.AccessControl;

var builder = DistributedApplication.CreateBuilder(args);
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

var auditApi = builder.AddProject<Projects.audit_api>("audit-api")
    .WithHttpEndpoint(port: 5003, name: "http", isProxied: false) // Explicit HTTP/1, NO PROXY
    .WithHttpEndpoint(port: 5203, name: "grpc", isProxied: false) // Explicit HTTP/2, NO PROXY
    .WithReference(auditDb_T1);

var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(Amazon.RegionEndpoint.USEast1);

var sqsQueue = builder.AddAWSCloudFormationTemplate(
        "violation-queue",
        "sqs-template.json")
    .WithReference(awsConfig);

var violationManagementApi = builder.AddProject<Projects.violation_management_api>("violation-management-api")
.WithHttpEndpoint(port: 5001, name: "http", isProxied: false) // Fixed port for BFF to connect
.WithReference(violationDb_T1)
.WithReference(auditApi)
.WithReference(redis)
.WithReference(sqsQueue)
.WithEnvironment("SQSConfig:QueueUrl", sqsQueue.GetOutput("ViolationQueueUrl"));

// Add the BFF (Gateway)
var bff = builder.AddProject<Projects.alpha_surveilance_bff>("bff")
    .WithHttpEndpoint(port: 5002, name: "http", isProxied: false) // Explicit HTTP/1, NO PROXY
    .WithHttpEndpoint(port: 5202, name: "grpc", isProxied: false) // Explicit HTTP/2, NO PROXY
    .WithReference(violationManagementApi)
    .WithReference(auditApi)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

var visionInferenceService = builder.AddPythonApp("vision-inference-service", "../../vision-inference-service", "main.py")
    .WithHttpEndpoint(name: "http", env: "PORT")
    .WithReference(sqsQueue)
    .WithEnvironment("SQS_QUEUE_URL", sqsQueue.GetOutput("ViolationQueueUrl")) // Inject the actual Queue URL
    .WithEnvironment("S3_BUCKET_NAME", builder.Configuration["S3Config:BucketName"])
    .WithEnvironment("AWS_REGION", builder.Configuration["AWS:Region"]);

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithReference(bff) // Frontend talks to BFF, not direct APIs
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();
    

builder.Build().Run();
