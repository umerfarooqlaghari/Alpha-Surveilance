using alpha_surveilance_bff.Hubs;
using alpha_surveilance_bff.Services;
using AlphaSurveilance.Audit.Grpc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Amazon.SimpleEmail;
using Amazon.Extensions.NETCore.Setup;

using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

var renderPort = Environment.GetEnvironmentVariable("PORT");

// Configure Kestrel to support local development ports and Render's dynamic port binding.
builder.WebHost.ConfigureKestrel(options =>
{
    if (int.TryParse(renderPort, out var port))
    {
        options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1AndHttp2);
        return;
    }

    // Port 5002: HTTP/1.1 (Standard API)
    options.ListenLocalhost(5002, o => o.Protocols = HttpProtocols.Http1);

    // Port 5202: HTTP/2 (Dedicated for gRPC)
    options.ListenLocalhost(5202, o => o.Protocols = HttpProtocols.Http2);
});

// 1. Add SignalR for real-time WebSockets
builder.Services.AddSignalR();
builder.Services.AddGrpc(); 

// 2. AWS SES for Emails
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonSimpleEmailService>();

// 3. Memory Cache for OTPs
builder.Services.AddMemoryCache();

// 4. App Services
// 4. App Services
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AuthHeaderHandler>();

// 5. JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // Disable default mapping to URIs
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!)),
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs/violations")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var token = context.Request.Headers["Authorization"].ToString();
                logger.LogWarning("JWT AUTH FAILED: {Error} | Path: {Path} | TokenPrefix: {TokenPrefix}",
                    context.Exception.Message,
                    context.Request.Path,
                    token.Length > 15 ? token.Substring(0, 15) : "Short/Missing");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT CHALLENGE: {Error} | {ErrorDescription} | Path: {Path}",
                    context.Error, context.ErrorDescription, context.Request.Path);
                return Task.CompletedTask;
            }
        };
    });

// 5b. Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => 
        policy.RequireClaim("role", "SuperAdmin"));
    options.AddPolicy("TenantAdmin", policy => 
        policy.RequireClaim("role", "TenantAdmin"));
});

// 6. gRPC Client to talk to the Audit Service
builder.Services.AddGrpcClient<AuditService.AuditServiceClient>(o =>
{
    var url = builder.Configuration["Services:AuditApi:GrpcUrl"] ?? "http://localhost:5203"; 
    if (url.StartsWith("tcp://")) url = url.Replace("tcp://", "http://");
    if (url.StartsWith("grpc://")) url = url.Replace("grpc://", "http://");
    o.Address = new Uri(url); 
});

// 7. HttpClient for Violation Management Service (REST)
builder.Services.AddHttpClient("ViolationApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ViolationApi:HttpUrl"] ?? "http://localhost:5001");
    
    var internalApiKey = builder.Configuration["InternalApi:ApiKey"];
    if (!string.IsNullOrEmpty(internalApiKey))
    {
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalApiKey);
    }
})
.AddHttpMessageHandler<AuthHeaderHandler>(); // ROBUST: Manual header propagation

builder.Services.AddCors(options =>
{
    options.AddPolicy("SurveilanceUiPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true) // Allow any origin for development
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); 
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Alpha Surveillance BFF", Version = "v1" });

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

var app = builder.Build();

// CRISIS DIAGNOSTICS: Check environment and config loading
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var envName = app.Environment.EnvironmentName;
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
var hasSecret = !string.IsNullOrEmpty(jwtSecret);
var secretPreview = hasSecret ? jwtSecret!.Substring(0, 5) + "..." : "MISSING";

logger.LogCritical("CRISIS STARTUP: Environment: {Env} | JwtSecretStatus: {HasSecret} | Preview: {Preview}", 
    envName, hasSecret, secretPreview);

if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var path = context.Request.Path;
    var hasAuth = context.Request.Headers.ContainsKey("Authorization");
    
    await next();
    
    if (context.Response.StatusCode == 401)
    {
        var user = context.User?.Identity?.IsAuthenticated ?? false;
        var userName = context.User?.Identity?.Name ?? "Anonymous";
        var roles = context.User?.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type.Contains("role")).Select(c => c.Value);
        
        logger.LogWarning("DIAGNOSTIC: 401 Unauthorized for {Path} | AuthHeader: {HasAuth} | IsAuthenticated: {IsAuth} | User: {User} | Roles: {Roles}", 
            path, hasAuth, user, userName, string.Join(",", roles ?? []));
    }
});

app.UseCors("SurveilanceUiPolicy");
// app.UseHeaderPropagation(); // Removed in favor of AuthHeaderHandler middleware-free approach

app.UseAuthentication(); // Enable Auth Middleware
app.UseAuthorization();

// Map Hubs & gRPC Services
app.MapHub<ViolationHub>("/hubs/violations");
app.MapGrpcService<NotificationGrpcService>();

app.MapControllers();

app.MapGet("/api/debug/config", (IConfiguration config, IWebHostEnvironment env) =>
{
    return Results.Ok(new {
        environment = env.EnvironmentName,
        jwtIssuer = config["Jwt:Issuer"] ?? "(empty)",
        jwtAudience = config["Jwt:Audience"] ?? "(empty)",
        jwtSecretLength = (config["Jwt:SecretKey"] ?? "").Length,
        aspnetcoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(not set)",
        dotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "(not set)",
    });
}).AllowAnonymous();

app.Run();
