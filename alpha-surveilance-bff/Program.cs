using alpha_surveilance_bff.Hubs;
using alpha_surveilance_bff.Services;
using AlphaSurveilance.Audit.Grpc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Amazon.SimpleEmail;
using Amazon.Extensions.NETCore.Setup;

using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to support HTTP/1 and HTTP/2 on dedicated ports
builder.WebHost.ConfigureKestrel(options =>
{
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
builder.Services.AddScoped<AuthService>();

// 5. JWT Authentication
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!))
        };
    });

// 5b. Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => 
        policy.RequireClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SuperAdmin"));
    options.AddPolicy("TenantAdmin", policy => 
        policy.RequireClaim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "TenantAdmin"));
});

// 6. gRPC Client to talk to the Audit Service
builder.Services.AddGrpcClient<AuditService.AuditServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5203"); 
});

// 7. HttpClient for Violation Management Service (REST)
builder.Services.AddHttpClient("ViolationApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5001");
})
.AddHeaderPropagation(); // Add header propagation to the client

// 8. Header Propagation Setup
builder.Services.AddHeaderPropagation(options =>
{
    options.Headers.Add("Authorization"); // Forward the JWT
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("SurveilanceUiPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:3000") 
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); 
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
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

if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SurveilanceUiPolicy");

app.UseHeaderPropagation(); // Enable header propagation middleware

app.UseAuthentication(); // Enable Auth Middleware
app.UseAuthorization();

// Map Hubs & gRPC Services
app.MapHub<ViolationHub>("/hubs/violations");
app.MapGrpcService<NotificationGrpcService>();

app.MapControllers();

app.Run();
