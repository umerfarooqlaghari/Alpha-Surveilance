using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class AuthService : IAuthService
{
    private readonly AppViolationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppViolationDbContext context,
        IJwtService jwtService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<AuthResponse> AuthenticateSuperAdminAsync(SuperAdminLoginRequest request)
    {
        // Find user by email with no tenant (SuperAdmin)
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == null);

        if (user == null)
        {
            _logger.LogWarning("SuperAdmin login failed: User not found for email {Email}", request.Email);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("SuperAdmin login failed: User {Email} is inactive", request.Email);
            throw new UnauthorizedAccessException("Account is inactive");
        }

        // Verify password using BCrypt
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("SuperAdmin login failed: Invalid password for {Email}", request.Email);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate JWT token
        var token = _jwtService.GenerateToken(user.Id, user.Email, "SuperAdmin", null);

        return new AuthResponse
        {
            Token = token,
            Role = "SuperAdmin",
            User = new UserInfo
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Designation = user.Designation
            },
            Tenant = null
        };
    }

    public async Task<AuthResponse> AuthenticateTenantAdminAsync(TenantAdminLoginRequest request)
    {
        // Find tenant by slug
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Slug == request.TenantSlug);

        if (tenant == null)
        {
            _logger.LogWarning("TenantAdmin login failed: Tenant not found for slug {Slug}", request.TenantSlug);
            throw new UnauthorizedAccessException("Invalid tenant slug");
        }

        if (tenant.Status != Core.Entities.TenantStatus.Active)
        {
            _logger.LogWarning("TenantAdmin login failed: Tenant {Slug} is not active", request.TenantSlug);
            throw new UnauthorizedAccessException("Tenant account is not active");
        }

        // Find user by email and tenant
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == tenant.Id);

        if (user == null)
        {
            _logger.LogWarning("TenantAdmin login failed: User not found for email {Email} in tenant {Slug}", 
                request.Email, request.TenantSlug);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("TenantAdmin login failed: User {Email} is inactive", request.Email);
            throw new UnauthorizedAccessException("Account is inactive");
        }

        // Verify password using BCrypt
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("TenantAdmin login failed: Invalid password for {Email}", request.Email);
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate JWT token
        var token = _jwtService.GenerateToken(user.Id, user.Email, "TenantAdmin", tenant.Id);

        return new AuthResponse
        {
            Token = token,
            Role = "TenantAdmin",
            User = new UserInfo
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Designation = user.Designation
            },
            Tenant = new TenantInfo
            {
                Id = tenant.Id,
                TenantName = tenant.TenantName,
                Slug = tenant.Slug,
                LogoUrl = tenant.LogoUrl
            }
        };
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        var principal = _jwtService.ValidateToken(token);
        if (principal == null) return false;

        var userId = _jwtService.GetUserIdFromToken(token);
        if (!userId.HasValue) return false;

        var user = await _context.Users.FindAsync(userId.Value);
        return user != null && user.IsActive;
    }
}
