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
        // Only apply key check to internal routes
        var isInternalPath = InternalApiPrefixes.Any(prefix =>
            context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

        if (!isInternalPath)
        {
            await _next(context);
            return;
        }

        // Validate header exists
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
        {
            _logger.LogWarning("Internal API call to {Path} rejected: missing {Header} header",
                context.Request.Path, ApiKeyHeader);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing internal API key" });
            return;
        }

        // Validate key value
        var configuredKey = _configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogError("InternalApi:ApiKey is not configured in appsettings");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Internal API not configured" });
            return;
        }

        if (!string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("Internal API call to {Path} rejected: invalid API key from {RemoteIp}",
                context.Request.Path, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid internal API key" });
            return;
        }

        await _next(context);
    }
}
