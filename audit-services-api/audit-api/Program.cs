using audit_api.Services;
using audit_api.Data;
using audit_api.Data.Repositories;
using audit_api.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

    // Port 5003: HTTP/1.1 (Standard API)
    options.ListenLocalhost(5003, o => o.Protocols = HttpProtocols.Http1);

    // Port 5203: HTTP/2 (Dedicated for gRPC)
    options.ListenLocalhost(5203, o => o.Protocols = HttpProtocols.Http2);
});

// 1. Database: TimescaleDB (PostgreSQL with Timescale Extension)
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("audit-logs")));

// 2. Repository Layer
builder.Services.AddScoped<IAuditRepository, AuditRepository>();

// 3. Security
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
builder.Services.AddAuthorization();

// Add services to the container.
builder.Services.AddGrpc(); // Enables gRPC support in the pipeline
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Automatically apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<AuditDbContext>();
    
    // Simple Retry Policy for Database Availability
    int retries = 5;
    while (retries > 0)
    {
        try
        {
            logger.LogInformation("⏳ Attempting to migrate database...");
            db.Database.Migrate(); // This ensures ALL missing migrations (including InitialCreate) are applied
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
                System.Threading.Thread.Sleep(3000);
            }
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// This is where we "Mount" our gRPC Service.
// It will now listen for binary requests on HTTP/2.
app.MapGrpcService<AuditGrpcService>();

// Education: Fallback route for browser users. 
// gRPC services cannot be called directly from a standard browser address bar.
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

app.MapControllers();

app.Run();
