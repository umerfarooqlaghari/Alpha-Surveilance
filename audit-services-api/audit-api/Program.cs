using audit_api.Services;
using audit_api.Data;
using audit_api.Data.Repositories;
using audit_api.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to support HTTP/1 and HTTP/2 on dedicated ports
builder.WebHost.ConfigureKestrel(options =>
{
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

// This is where we "Mount" our gRPC Service.
// It will now listen for binary requests on HTTP/2.
app.MapGrpcService<AuditGrpcService>();

// Education: Fallback route for browser users. 
// gRPC services cannot be called directly from a standard browser address bar.
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

app.MapControllers();

app.Run();
