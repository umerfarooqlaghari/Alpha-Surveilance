using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class UserService : IUserService
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(AppViolationDbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request)
    {
        // Check if email already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            throw new InvalidOperationException($"User with email '{request.Email}' already exists");
        }

        // Validate tenant exists if TenantId is provided
        if (request.TenantId.HasValue)
        {
            var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId.Value);
            if (!tenantExists)
            {
                throw new InvalidOperationException($"Tenant with ID '{request.TenantId}' not found");
            }
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            FullName = request.FullName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            EmployeeCode = request.EmployeeCode,
            Designation = request.Designation,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        // Assign roles
        if (request.RoleIds.Any())
        {
            foreach (var roleId in request.RoleIds)
            {
                var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
                if (roleExists)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = roleId,
                        AssignedAt = DateTime.UtcNow
                    });
                }
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Created user {UserId} with email {Email}", user.Id, user.Email);

        // Reload with roles
        var createdUser = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == user.Id);

        return UserResponse.FromEntity(createdUser);
    }

    public async Task<List<UserResponse>> GetUsersByTenantAsync(Guid? tenantId)
    {
        var query = _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (tenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == tenantId.Value);
        }

        var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

        return users.Select(UserResponse.FromEntity).ToList();
    }

    public async Task<UserResponse?> GetUserByIdAsync(Guid id)
    {
        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        return user == null ? null : UserResponse.FromEntity(user);
    }

    public async Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return null;

        if (!string.IsNullOrEmpty(request.FullName))
            user.FullName = request.FullName;

        if (!string.IsNullOrEmpty(request.PhoneNumber))
            user.PhoneNumber = request.PhoneNumber;

        if (request.EmployeeCode != null)
            user.EmployeeCode = request.EmployeeCode;

        if (request.Designation != null)
            user.Designation = request.Designation;

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated user {UserId}", id);

        var updatedUser = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == id);

        return UserResponse.FromEntity(updatedUser);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Reset password for user {UserId}", id);

        return true;
    }

    public async Task<bool> ToggleUserStatusAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return false;

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Toggled status for user {UserId} to {IsActive}", id, user.IsActive);

        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return false;

        // Soft delete by deactivating
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Soft deleted user {UserId}", id);

        return true;
    }
}
