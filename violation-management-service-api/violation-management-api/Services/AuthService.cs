using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
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

    public async Task<AuthResponse> RegisterTenantAsync(TenantSignupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName))
            throw new InvalidOperationException("Organization name is required.");

        if (string.IsNullOrWhiteSpace(request.Slug))
            throw new InvalidOperationException("Organization slug is required.");

        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            throw new InvalidOperationException("Admin email is required.");

        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 6)
            throw new InvalidOperationException("Admin password must be at least 6 characters.");

        var slug = request.Slug.Trim().ToLowerInvariant().Replace(" ", "-");

        // Validate slug uniqueness
        if (await _context.Tenants.AnyAsync(t => t.Slug == slug))
        {
            throw new InvalidOperationException($"An organization with slug '{slug}' already exists.");
        }

        var normalizedEmail = request.AdminEmail.Trim().ToLowerInvariant();

        // Validate email uniqueness across users
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
        {
            throw new InvalidOperationException($"A user with email '{request.AdminEmail}' already exists.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantName = request.TenantName.Trim(),
            Slug = slug,
            EmployeeCount = request.EmployeeCount > 0 ? request.EmployeeCount : 1,
            Address = request.Address ?? string.Empty,
            City = request.City ?? string.Empty,
            Country = request.Country ?? string.Empty,
            Industry = request.Industry ?? string.Empty,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            FullName = string.IsNullOrWhiteSpace(request.AdminFullName) ? "Tenant Admin" : request.AdminFullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = request.PhoneNumber ?? string.Empty,
            Designation = "Organization Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        // Assign TenantAdmin role if exists
        var tenantAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "TenantAdmin");
        if (tenantAdminRole != null)
        {
            _context.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = tenantAdminRole.Id,
                AssignedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Successfully registered new tenant {Slug} and admin {Email}", tenant.Slug, user.Email);

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
}

