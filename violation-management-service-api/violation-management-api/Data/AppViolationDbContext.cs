using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Core.Domain;
using violation_management_api.Core.Entities;

namespace AlphaSurveilance.Data
{
    public class AppViolationDbContext(DbContextOptions<AppViolationDbContext> options) : DbContext(options)
    {
        // Existing DbSets
        public DbSet<Violation> Violations { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        
        // New Multi-Tenant DbSets
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Camera> Cameras { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // ===== Violation Configuration =====
            modelBuilder.Entity<Violation>()
                .HasIndex(v => v.TenantId);

            modelBuilder.Entity<Violation>()
                .HasIndex(v => v.CorrelationId)
                .IsUnique();

            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(o => o.ProcessedAt);
            
            // ===== Tenant Configuration =====
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.HasKey(t => t.Id);
                
                entity.HasIndex(t => t.Slug)
                    .IsUnique();
                
                entity.Property(t => t.TenantName)
                    .IsRequired()
                    .HasMaxLength(200);
                
                entity.Property(t => t.Slug)
                    .IsRequired()
                    .HasMaxLength(100);
                
                entity.Property(t => t.Address)
                    .HasMaxLength(500);
                
                entity.Property(t => t.City)
                    .IsRequired()
                    .HasMaxLength(100);
                
                entity.Property(t => t.Country)
                    .IsRequired()
                    .HasMaxLength(100);
                
                entity.Property(t => t.Industry)
                    .HasMaxLength(100);
            });
            
            // ===== User Configuration =====
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                
                entity.HasIndex(u => u.Email)
                    .IsUnique();
                
                entity.Property(u => u.FullName)
                    .IsRequired()
                    .HasMaxLength(200);
                
                entity.Property(u => u.Email)
                    .IsRequired()
                    .HasMaxLength(256);
                
                entity.Property(u => u.PhoneNumber)
                    .HasMaxLength(20);
                
                entity.Property(u => u.EmployeeCode)
                    .HasMaxLength(50);
                
                entity.Property(u => u.Designation)
                    .HasMaxLength(100);
                
                // Foreign Key to Tenant (nullable for SuperAdmin)
                entity.HasOne(u => u.Tenant)
                    .WithMany(t => t.Users)
                    .HasForeignKey(u => u.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            // ===== Camera Configuration =====
            modelBuilder.Entity<Camera>(entity =>
            {
                entity.HasKey(c => c.Id);
                
                entity.HasIndex(c => c.CameraId)
                    .IsUnique();
                
                entity.Property(c => c.CameraId)
                    .IsRequired()
                    .HasMaxLength(100);
                
                entity.Property(c => c.Name)
                    .IsRequired()
                    .HasMaxLength(200);
                
                entity.Property(c => c.Location)
                    .HasMaxLength(300);
                
                entity.Property(c => c.RtspUrlEncrypted)
                    .IsRequired();
                
                // Foreign Key to Tenant
                entity.HasOne(c => c.Tenant)
                    .WithMany(t => t.Cameras)
                    .HasForeignKey(c => c.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            
            // ===== Role Configuration =====
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(r => r.Id);
                
                entity.HasIndex(r => r.Name)
                    .IsUnique();
                
                entity.Property(r => r.Name)
                    .IsRequired()
                    .HasMaxLength(50);
                
                entity.Property(r => r.Description)
                    .HasMaxLength(500);
            });
            
            // ===== Permission Configuration =====
            modelBuilder.Entity<Permission>(entity =>
            {
                entity.HasKey(p => p.Id);
                
                entity.HasIndex(p => p.Name)
                    .IsUnique();
                
                entity.Property(p => p.Name)
                    .IsRequired()
                    .HasMaxLength(100);
                
                entity.Property(p => p.Resource)
                    .IsRequired()
                    .HasMaxLength(50);
                
                entity.Property(p => p.Action)
                    .IsRequired()
                    .HasMaxLength(50);
                
                entity.Property(p => p.Description)
                    .HasMaxLength(500);
            });
            
            // ===== UserRole Configuration (Many-to-Many) =====
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(ur => new { ur.UserId, ur.RoleId });
                
                entity.HasOne(ur => ur.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                entity.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            
            // ===== RolePermission Configuration (Many-to-Many) =====
            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });
                
                entity.HasOne(rp => rp.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(rp => rp.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                entity.HasOne(rp => rp.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(rp => rp.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}