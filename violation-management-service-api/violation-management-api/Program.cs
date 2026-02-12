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
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using AlphaSurveilance.Audit.Grpc;
using AlphaSurveilance.Bff.Grpc;
using AlphaSurveilance;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// AWS Services (SQS only - SES removed)
builder.Services.AddAWSService<IAmazonSQS>();

// HttpClient for Brevo and others
builder.Services.AddHttpClient();

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
builder.Services.AddScoped<IEmailService, BrevoEmailService>();

// Multi-Tenant Management Services
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICameraService, CameraService>();

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
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!))
        };
    });

// Security: Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => 
        policy.RequireClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SuperAdmin"));
    options.AddPolicy("TenantAdmin", policy => 
        policy.RequireClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "TenantAdmin"));
});


// ... other services

// High-Performance gRPC Client Registration
// Instead of slow JSON/HTTP, we now "Plug In" to the Audit Service using gRPC.
builder.Services.AddGrpcClient<AuditService.AuditServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5203"); // New Dedicated HTTP/2 Port
});

// Real-Time Notification gRPC Client (Talks to the BFF)
builder.Services.AddGrpcClient<NotificationService.NotificationServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5202"); // New Dedicated HTTP/2 Port
});

builder.Services.AddScoped<IAuditApiClient, AuditApiClient>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
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

app.UseHttpsRedirection();
app.UseRateLimiter(); // Apply Rate Limiting

app.MapHealthChecks("/health");

app.UseAuthentication(); // Enable JWT Authentication
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
            logger.LogInformation("✅ Database seeded successfully.");

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
            }
            else
            {
                Thread.Sleep(3000);
            }
        }
    }
}

app.Run();
