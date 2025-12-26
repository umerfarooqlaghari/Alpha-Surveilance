using System.Security.AccessControl;

var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("violation-db-server-1")
.WithPgAdmin()
.WithDataVolume();

var violationDb_T1 = postgres.AddDatabase("violations");

var auditPostgres = builder.AddPostgres("audit-db-server-1")
    .WithImage("timescale/timescaledb", "latest-pg16") // Custom Timescale image
    .WithPgAdmin() // You can attach pgAdmin to this server too
    .WithDataVolume();

var auditDb_T1 = auditPostgres.AddDatabase("audit-logs");
var redis = builder.AddRedis("cache"); 
// var awsConfig = builder.AddAWSSDKConfig()
//     .WithProfile("your-profile")
//     .WithRegion(RegionEndpoint.USEast1);

// var s3Bucket = builder.AddAWSCloudFormationTemplate("ViolationsBucket", "s3-template.json");
// var sqsQueue = builder.AddAWSCloudFormationTemplate("ViolationsQueue", "sqs-template.json");    

var auditApi = builder.AddProject<Projects.audit_api>("audit-api")
.WithReference(auditDb_T1);

var violationManagementApi = builder.AddProject<Projects.violation_management_api>("violation-management-api")
.WithReference(auditDb_T1)
.WithReference(auditApi)
.WithReference(redis);
// .WithReference(s3Bucket)
// .WithReference(sqsQueue);
// var visionInference = builder.AddPythonApp("vision-inference", "../../vision-inference-service", "main.py");


//Commands when python service is made.
// var visionInference = builder.AddProject<Projects.vision_inference_service>("vision-inference-service");

var frontend = builder.AddNpmApp("frontend", "../../surveilance-ui", "dev")
    .WithReference(violationManagementApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();
    

builder.Build().Run();
