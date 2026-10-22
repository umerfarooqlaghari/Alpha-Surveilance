using Microsoft.AspNetCore.Mvc;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services;
using violation_management_api.Services.Interfaces;


namespace violation_management_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICloudflareService _cloudflareService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ICloudflareService cloudflareService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _cloudflareService = cloudflareService;
        _logger = logger;
    }

    [HttpPost("superadmin/login")]
    public async Task<IActionResult> SuperAdminLogin([FromBody] SuperAdminLoginRequest request)
    {
        try
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var isTurnstileValid = await _cloudflareService.VerifyTurnstileTokenAsync(request.TurnstileToken, remoteIp);
            if (!isTurnstileValid)
            {
                return BadRequest(new { error = "Turnstile security verification failed. Please complete the challenge." });
            }

            var response = await _authService.AuthenticateSuperAdminAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SuperAdmin login");
            return StatusCode(500, new { error = "An error occurred during login" });
        }
    }

    [HttpPost("tenant/login")]
    public async Task<IActionResult> TenantAdminLogin([FromBody] TenantAdminLoginRequest request)
    {
        try
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var isTurnstileValid = await _cloudflareService.VerifyTurnstileTokenAsync(request.TurnstileToken, remoteIp);
            if (!isTurnstileValid)
            {
                return BadRequest(new { error = "Turnstile security verification failed. Please complete the challenge." });
            }

            var response = await _authService.AuthenticateTenantAdminAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TenantAdmin login");
            return StatusCode(500, new { error = "An error occurred during login" });
        }
    }

    [HttpPost("tenant/signup")]
    [HttpPost("signup")]
    public async Task<IActionResult> TenantSignup([FromBody] TenantSignupRequest request)
    {
        try
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var isTurnstileValid = await _cloudflareService.VerifyTurnstileTokenAsync(request.TurnstileToken, remoteIp);
            if (!isTurnstileValid)
            {
                return BadRequest(new { error = "Turnstile security verification failed. Please complete the challenge." });
            }

            var response = await _authService.RegisterTenantAsync(request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Tenant signup");
            return StatusCode(500, new { error = "An error occurred during signup" });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken([FromBody] ValidateTokenRequest request)
    {
        try
        {
            var isValid = await _authService.ValidateTokenAsync(request.Token);
            return Ok(new { valid = isValid });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return StatusCode(500, new { error = "An error occurred during token validation" });
        }
    }
}
