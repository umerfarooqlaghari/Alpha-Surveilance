using System.Security.Claims;

namespace violation_management_api.Middleware;

/// <summary>
/// Middleware that validates X-Internal-Api-Key header for service-to-service requests.
/// Protects: /api/cameras/internal/* and /api/violations/internal/*
/// This is a service-to-service authentication mechanism (not JWT) used by the Vision Inference Service.
/// </summary>
public class InternalApiKeyMiddleware
{
    private static readonly string[] InternalApiPrefixes =
    [
        "/api/cameras/internal",
        "/api/violations/internal",
    ];
    private const string ApiKeyHeader = "X-Internal-Api-Key";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalApiKeyMiddleware> _logger;

    public InternalApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<InternalApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var configuredKey = _configuration["InternalApi:ApiKey"];

        // If a key is provided, check if it's the valid internal key
        if (context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
        {
            if (!string.IsNullOrWhiteSpace(configuredKey) && string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
            {
                // Key is valid - Elevate privileges
                var claims = new[] {
                    new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SuperAdmin"),
                    new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "TenantAdmin"),
                    new Claim(ClaimTypes.Role, "SuperAdmin"),
                    new Claim(ClaimTypes.Role, "TenantAdmin"),
                    new Claim(ClaimTypes.Email, "internal@system.local"),
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
                };
                var identity = new ClaimsIdentity(claims, "InternalApiKey");
                // Set the current user to this elevated principal
                context.User = new ClaimsPrincipal(identity);
                
                await _next(context);
                return;
            }
        }

        // Only apply strict rejection if it's an internal route that failed the check
        var isInternalPath = InternalApiPrefixes.Any(prefix =>
            context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

        if (!isInternalPath)
        {
            await _next(context);
            return;
        }

        // If we reach here for an internal path, the key was missing or invalid
        if (!context.Request.Headers.ContainsKey(ApiKeyHeader))
        {
            _logger.LogWarning("Internal API call to {Path} rejected: missing {Header} header",
                context.Request.Path, ApiKeyHeader);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing internal API key" });
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogWarning("Internal API Key not configured in appsettings. Internal request rejected to prevent bypass.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Internal API key not configured on server." });
            return;
        }

        _logger.LogWarning("Internal API call to {Path} rejected: invalid API key from {RemoteIp}",
            context.Request.Path, context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid internal API key" });
        return;
    }
}
