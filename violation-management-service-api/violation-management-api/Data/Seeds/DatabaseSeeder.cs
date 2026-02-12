using AlphaSurveilance.Data;
using Microsoft.EntityFrameworkCore;
using violation_management_api.Core.Entities;

namespace AlphaSurveilance.Data.Seeds
{
    public static class DatabaseSeeder
    {
        public static async Task SeedAsync(AppViolationDbContext context)
        {
            // 1. Seed Roles
            await SeedRolesAsync(context);

            // 2. Seed SuperAdmin User
            await SeedSuperAdminAsync(context);
        }

        private static async Task SeedRolesAsync(AppViolationDbContext context)
        {
            var roles = new[] { "SuperAdmin", "TenantAdmin", "Employee" };

            foreach (var roleName in roles)
            {
                var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role == null)
                {
                    role = new Role
                    {
                        Id = Guid.NewGuid(),
                        Name = roleName,
                        Description = $"Role for {roleName}",
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Roles.AddAsync(role);
                }
            }
            await context.SaveChangesAsync();
        }

        private static async Task SeedSuperAdminAsync(AppViolationDbContext context)
        {
            var superAdminEmail = "info@alpha-devs.cloud";
            
            // Check if SuperAdmin user already exists
            var existingUser = await context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == superAdminEmail);

            if (existingUser == null)
            {
                // Create SuperAdmin User
                var superAdmin = new User
                {
                    Id = Guid.NewGuid(),
                    Email = superAdminEmail,
                    FullName = "Super Admin system",
                    // BCrypt Hash for "132VanDijk@!"
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("132VanDijk@!"),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    TenantId = null // SuperAdmin has no tenant
                };

                await context.Users.AddAsync(superAdmin);
                await context.SaveChangesAsync(); // Save user to get ID (though we set it manually)

                // Assign SuperAdmin Role
                var superAdminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin");
                if (superAdminRole != null)
                {
                    var userRole = new UserRole
                    {
                        UserId = superAdmin.Id,
                        RoleId = superAdminRole.Id,
                        AssignedAt = DateTime.UtcNow
                    };
                    await context.UserRoles.AddAsync(userRole);
                    await context.SaveChangesAsync();
                }
                
                Console.WriteLine("✅ SuperAdmin user seeded successfully.");
            }
            else
            {
                // Ensure existing user has SuperAdmin role
                var hasRole = existingUser.UserRoles.Any(ur => ur.Role.Name == "SuperAdmin");
                if (!hasRole)
                {
                    var superAdminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin");
                    if (superAdminRole != null)
                    {
                        var userRole = new UserRole
                        {
                            UserId = existingUser.Id,
                            RoleId = superAdminRole.Id, // Fixed: Use RoleId, not Id from role object directly called RoleId
                            AssignedAt = DateTime.UtcNow
                        };
                        await context.UserRoles.AddAsync(userRole);
                        await context.SaveChangesAsync();
                        Console.WriteLine("⚠️ Added missing SuperAdmin role to existing user.");
                    }
                }
                Console.WriteLine("ℹ️ SuperAdmin user already exists.");
            }
        }
    }
}

