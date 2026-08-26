using AlphaSurveilance.Data;
using AlphaSurveilance.Data.Repositories;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.Services;
using AlphaSurveilance.Services.Interfaces;
using AlphaSurveilance.Mappings;
using AlphaSurveilance.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Amazon.SQS;
using violation_management_api.Services;
using violation_management_api.Services.Interfaces;
using violation_management_api.Middleware;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using AlphaSurveilance.Audit.Grpc;
using AlphaSurveilance.Bff.Grpc;
using AlphaSurveilance;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using violation_management_api.Core.Entities;

// === Crash diagnostics: write to stderr so output survives even if ILogger is broken ===
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    Console.Error.WriteLine($"[FATAL][UnhandledException] IsTerminating={e.IsTerminating} Exception={e.ExceptionObject}");
    Console.Error.Flush();
};
TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    Console.Error.WriteLine($"[FATAL][UnobservedTaskException] {e.Exception}");
    Console.Error.Flush();
    e.SetObserved();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Console.Error.WriteLine("[DIAG] ProcessExit raised");
    Console.Error.Flush();
};
Console.Error.WriteLine($"[DIAG] Process starting. .NET={Environment.Version} OS={Environment.OSVersion} Arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
Console.Error.Flush();

var builder = WebApplication.CreateBuilder(args);

// Allow plaintext HTTP/2 (h2c) only in Development. In Production (Render),
// gRPC must go over HTTPS, so this switch must NOT be enabled.
if (builder.Environment.IsDevelopment())
{
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
}

var renderPort = Environment.GetEnvironmentVariable("PORT");
var backgroundServicesEnabled = !string.Equals(
    builder.Configuration["DISABLE_BACKGROUND_SERVICES"],
    "true",
    StringComparison.OrdinalIgnoreCase);

builder.WebHost.ConfigureKestrel(options =>
{
    if (int.TryParse(renderPort, out var port))
    {
        options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1AndHttp2);
    }
});

// AWS Services
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddAWSService<Amazon.SimpleEmail.IAmazonSimpleEmailService>();
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();

// HttpClient for Brevo and others
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// Database
builder.Services.AddDbContext<AppViolationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("violations"), o => o.UseVector());
});

// Mappings
builder.Services.AddAutoMapper(typeof(MappingProfile));

// Layers: Repository -> Service -> Controller
builder.Services.AddScoped<IViolationRepository, ViolationRepository>();
builder.Services.AddScoped<IViolationService, ViolationService>();
builder.Services.AddSingleton<IFramePresignService, S3FramePresignService>();
builder.Services.AddScoped<IAuditApiClient, AuditApiClient>();
builder.Services.AddScoped<ISqsQueueService, SqsQueueService>();

// Email Services (Brevo only - SES removed)
builder.Services.AddScoped<EmailDispatcherService>();
// builder.Services.AddScoped<IEmailService, BrevoEmailService>();
builder.Services.AddScoped<IEmailService, SesEmailService>();


// Multi-Tenant Management Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICameraService, CameraService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IEdgeDeviceService, EdgeDeviceService>();
builder.Services.AddScoped<ISopService, SopService>();
builder.Services.AddScoped<ITenantViolationRequestService, TenantViolationRequestService>();
builder.Services.AddScoped<ICloudflareService, CloudflareService>();
builder.Services.AddScoped<IAiModelService, AiModelService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IReIdService, ReIdService>();


// Authentication Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Background Services
if (backgroundServicesEnabled)
{
    builder.Services.AddHostedService<ViolationWorkerService>();
    builder.Services.AddHostedService<OutboxProcessorService>();
}

// Security: Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);
        opt.PermitLimit = 50;
        opt.QueueLimit = 10;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// Security: JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!)),
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });

// 5b. Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => 
        policy.RequireClaim("role", "SuperAdmin"));
    options.AddPolicy("TenantAdmin", policy => 
        policy.RequireClaim("role", "TenantAdmin"));
    options.AddPolicy("SuperOrTenantAdmin", policy => 
        policy.RequireClaim("role", "SuperAdmin", "TenantAdmin"));
});


// ... other services

// High-Performance gRPC Client Registration
// Instead of slow JSON/HTTP, we now "Plug In" to the Audit Service using gRPC.
builder.Services.AddGrpcClient<AuditService.AuditServiceClient>(o =>
{
    var url = builder.Configuration["Services:AuditApi:GrpcUrl"] ?? "http://localhost:5203";
    if (url.StartsWith("tcp://")) url = url.Replace("tcp://", "http://");
    if (url.StartsWith("grpc://")) url = url.Replace("grpc://", "http://");
    o.Address = new Uri(url); // New Dedicated HTTP/2 Port
});

// Real-Time Notification gRPC Client (Talks to the BFF)
builder.Services.AddGrpcClient<NotificationService.NotificationServiceClient>(o =>
{
    var url = builder.Configuration["Services:Bff:GrpcUrl"] ?? "http://localhost:5202";
    if (url.StartsWith("tcp://")) url = url.Replace("tcp://", "http://");
    if (url.StartsWith("grpc://")) url = url.Replace("grpc://", "http://");
    o.Address = new Uri(url); // New Dedicated HTTP/2 Port
});

builder.Services.AddScoped<IAuditApiClient, AuditApiClient>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();
// builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Violation Management API", Version = "v1" });

    // Use HTTP Bearer scheme (Automatic "Bearer " prefix)
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token in the text input below.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppViolationDbContext>();
    // .AddRedis(builder.Configuration.GetConnectionString("cache")); // Temporarily commented if not used

var app = builder.Build();
app.Logger.LogInformation("Background services enabled: {Enabled}", backgroundServicesEnabled);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter(); // Apply Rate Limiting

app.MapHealthChecks("/health");

app.UseAuthentication(); // Enable JWT Authentication FIRST
app.UseMiddleware<InternalApiKeyMiddleware>(); // Internal API Key AFTER JWT (optional bypass)
app.UseAuthorization();
app.MapControllers();

// Auto-Migration & Seed on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<AppViolationDbContext>();
    
    try
    {
        if (db.Database.IsRelational())
        {
            logger.LogInformation("Applying database migrations...");
            db.Database.Migrate();
        }
        
        // Seed standard roles & superadmin if not present
        await AlphaSurveilance.Data.Seeds.DatabaseSeeder.SeedAsync(db);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or startup seeding.");
    }
}

app.Run();
