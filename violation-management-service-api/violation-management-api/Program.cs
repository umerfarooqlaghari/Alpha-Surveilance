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

var builder = WebApplication.CreateBuilder(args);
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

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
    options.UseNpgsql(builder.Configuration.GetConnectionString("violations"));
});

// Mappings
builder.Services.AddAutoMapper(typeof(MappingProfile));

// Layers: Repository -> Service -> Controller
builder.Services.AddScoped<IViolationRepository, ViolationRepository>();
builder.Services.AddScoped<IViolationService, ViolationService>();
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
builder.Services.AddScoped<ISopService, SopService>();
builder.Services.AddScoped<ITenantViolationRequestService, TenantViolationRequestService>();
builder.Services.AddScoped<ICloudflareService, CloudflareService>();

// Authentication Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Background Services
builder.Services.AddHostedService<ViolationWorkerService>();
builder.Services.AddHostedService<OutboxProcessorService>();

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

// Auto-Migration (Added for Aspire)
// Auto-Migration (Added for Aspire)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<AppViolationDbContext>();
    
    // Simple Retry Policy for Database Availability
    int retries = 5;
    while (retries > 0)
    {
        try
        {
            logger.LogInformation("⏳ Attempting to migrate database...");
            db.Database.Migrate(); // This ensures ALL missing migrations (including InitialCreate) are applied
            
            // Seed Database
            logger.LogInformation("🌱 Seeding database...");
            await AlphaSurveilance.Data.Seeds.DatabaseSeeder.SeedAsync(db);
            
            var sopId = Guid.NewGuid();
            var sopViolId = Guid.NewGuid();

            if (!db.Sops.Any(s => s.Name == "Human Detection"))
            {
                db.Sops.Add(new violation_management_api.Core.Entities.Sop { Id = sopId, Name = "Human Detection", CreatedAt = DateTime.UtcNow });
                db.SopViolationTypes.Add(new violation_management_api.Core.Entities.SopViolationType 
                { 
                    Id = sopViolId, SopId = sopId, Name = "Unauthorized Person", 
                    ModelIdentifier = "hustvl/yolos-tiny", TriggerLabels = "[\"person\"]"
                });

                var cam = db.Cameras.FirstOrDefault(c => c.CameraId == "CAM-001");
                if (cam != null)
                {
                    db.CameraViolationTypes.Add(new violation_management_api.Core.Entities.CameraViolationType 
                    { 
                        CameraId = cam.Id, SopViolationTypeId = sopViolId 
                    });
                }
                db.SaveChanges();
            }

            logger.LogInformation("✅ Database seeded successfully.");

            // ── Construction Site Safety SOP ──────────────────────────────────────
            if (!db.Sops.Any(s => s.Name == "Construction Site Safety"))
            {
                var constructionSopId = Guid.NewGuid();
                db.Sops.Add(new violation_management_api.Core.Entities.Sop 
                { 
                    Id = constructionSopId, 
                    Name = "Construction Site Safety", 
                    CreatedAt = DateTime.UtcNow 
                });

                var violationTypes = new[]
                {
                    new violation_management_api.Core.Entities.SopViolationType
                    {
                        Id = Guid.NewGuid(), SopId = constructionSopId,
                        Name = "No Hardhat",
                        ModelIdentifier = "construction-site-safety/1",
                        TriggerLabels = "[\"no-hardhat\"]"
                    },
                    new violation_management_api.Core.Entities.SopViolationType
                    {
                        Id = Guid.NewGuid(), SopId = constructionSopId,
                        Name = "No Safety Vest",
                        ModelIdentifier = "construction-site-safety/1",
                        TriggerLabels = "[\"no-safety vest\"]"
                    },
                    new violation_management_api.Core.Entities.SopViolationType
                    {
                        Id = Guid.NewGuid(), SopId = constructionSopId,
                        Name = "No Mask / No Face Cover",
                        ModelIdentifier = "construction-site-safety/1",
                        TriggerLabels = "[\"no-mask\"]"
                    }
                };

                db.SopViolationTypes.AddRange(violationTypes);
                db.SaveChanges();
                logger.LogInformation("🏗️ Construction Site Safety SOP seeded.");
            }

            // ── Kitchen Hygiene SOP ──────────────────────────────────────────
            var kitchenSop = db.Sops.FirstOrDefault(s => s.Name == "Kitchen Hygiene");
            if (kitchenSop == null)
            {
                kitchenSop = new violation_management_api.Core.Entities.Sop 
                { 
                    Id = Guid.NewGuid(), 
                    Name = "Kitchen Hygiene", 
                    CreatedAt = DateTime.UtcNow 
                };
                db.Sops.Add(kitchenSop);
                db.SaveChanges();
            }

            // Ensure all 3 hygiene violation types exist with correct ModelIdentifiers
            var existingHygieneRules = db.SopViolationTypes.Where(v => v.SopId == kitchenSop.Id).ToList();
            
            var targetHygieneRules = new[]
            {
                new { Name = "No Hairnet", Label = "person without hairnet" },
                new { Name = "No Gloves", Label = "person without gloves" },
                new { Name = "No Mask / No Face Cover", Label = "person without mask" }
            };

            foreach (var target in targetHygieneRules)
            {
                var rule = existingHygieneRules.FirstOrDefault(r => r.Name == target.Name);
                if (rule == null)
                {
                    db.SopViolationTypes.Add(new violation_management_api.Core.Entities.SopViolationType
                    {
                        Id = Guid.NewGuid(),
                        SopId = kitchenSop.Id,
                        Name = target.Name,
                        ModelIdentifier = "restaurant-hygiene-v1",
                        TriggerLabels = $"[\"{target.Label}\"]"
                    });
                }
                else
                {
                    // Update existing to use the new logical model and labels
                    rule.ModelIdentifier = "restaurant-hygiene-v1";
                    rule.TriggerLabels = $"[\"{target.Label}\"]";
                }
            }
            db.SaveChanges();
            logger.LogInformation("🍳 Kitchen Hygiene SOP synchronized (Hairnet, Gloves, Mask).");

            // ── Seed Notification Emails for Demo ────────────────────────────────
            var demoTenantId = Guid.Parse("97db6efb-5545-4152-96ff-5da731fa17d5");
            if (!db.TenantNotificationEmails.Any(e => e.TenantId == demoTenantId))
            {
                db.TenantNotificationEmails.Add(new violation_management_api.Core.Entities.TenantNotificationEmail
                {
                    Id = Guid.NewGuid(),
                    TenantId = demoTenantId,
                    Email = "info@alpha-devs.cloud", // Primary stakeholder for demo
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
                logger.LogInformation("📧 Demo notification email seeded.");
            }

            logger.LogInformation("✅ Database migration applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            retries--;
            logger.LogWarning(ex, "⚠️ Migration failed. Retrying in 3 seconds... ({Retries} left)", retries);
            if (retries == 0)
            {
                logger.LogError("❌ Migration failed permanently. Error: {Message}", ex.Message);
                throw; // Crash the app so the user knows migration failed
            }
            else
            {
                Thread.Sleep(3000);
            }
        }
    }
}

app.Run();
