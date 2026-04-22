using System.Net.Http.Headers;

namespace alpha_surveilance_bff.Services;

/// <summary>
/// A message handler that automatically propagates Authorization and X-Tenant-Id headers
/// from the current incoming HTTP request to the outgoing request.
/// This is much more reliable than the standard ASP.NET Core HeaderPropagation middleware
/// in complex multi-service environments.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthHeaderHandler> _logger;

    public AuthHeaderHandler(IHttpContextAccessor httpContextAccessor, ILogger<AuthHeaderHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null)
        {
            // 1. Forward Authorization Header
            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                // Clear any existing to avoid duplicates, then add
                request.Headers.Remove("Authorization");
                request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
            }

            // 2. Forward X-Tenant-Id Header
            if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdHeader))
            {
                request.Headers.Remove("X-Tenant-Id");
                request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantIdHeader.ToString());
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
