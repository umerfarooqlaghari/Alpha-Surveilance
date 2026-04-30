using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Core.Domain;
using violation_management_api.Core.Entities;
using AlphaSurveilance.Models;

namespace AlphaSurveilance.Data
{
    public class AppViolationDbContext(DbContextOptions<AppViolationDbContext> options) : DbContext(options)
    {
        public DbSet<Employee> Employees { get; set; }

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
        public DbSet<EmailTemplate> EmailTemplates { get; set; }

        public DbSet<Sop> Sops { get; set; }
        public DbSet<SopViolationType> SopViolationTypes { get; set; }
        public DbSet<TenantViolationRequest> TenantViolationRequests { get; set; }
        public DbSet<CameraViolationType> CameraViolationTypes { get; set; }
        public DbSet<TenantNotificationEmail> TenantNotificationEmails { get; set; }
        public DbSet<FileManagerFolder> FileManagerFolders { get; set; }
        public DbSet<FileManagerFile> FileManagerFiles { get; set; }
        public DbSet<ViolationAudit> ViolationAudits { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // ===== Employee Configuration =====
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasKey(e => e.Id);

                // Partitioning Key Index
                entity.HasIndex(e => e.TenantId);

                // Unique Constraints per Tenant
                entity.HasIndex(e => new { e.TenantId, e.Email })
                    .IsUnique();

                entity.HasIndex(e => new { e.TenantId, e.EmployeeId })
                    .IsUnique();

                entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
                entity.Property(e => e.EmployeeId).IsRequired().HasMaxLength(100);
                
                // Configure MetadataJson as jsonb (PostgreSQL)
                entity.Property(e => e.MetadataJson).HasColumnType("jsonb");
                entity.Property(e => e.FaceScanStatus).HasConversion<string>();
            });

            // ===== Violation Configuration =====
            modelBuilder.Entity<Violation>(entity =>
            {
                entity.HasIndex(v => v.TenantId);
                
                entity.HasOne(v => v.SopViolationType)
                    .WithMany(sv => sv.Violations)
                    .HasForeignKey(v => v.SopViolationTypeId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Tenant>()
                .HasMany(t => t.Violations)
                .WithOne()
                .HasForeignKey(v => v.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== ViolationAudit Configuration =====
            modelBuilder.Entity<ViolationAudit>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.HasIndex(a => a.ViolationId).IsUnique(); // one audit per violation
                entity.HasIndex(a => a.TenantId);

                entity.HasOne(a => a.Violation)
                    .WithOne()
                    .HasForeignKey<ViolationAudit>(a => a.ViolationId)
                    .OnDelete(DeleteBehavior.Cascade);

                // All text fields — no max length enforced for long-form descriptions
                entity.Property(a => a.ExecutiveSummary).HasColumnType("text");
                entity.Property(a => a.RootCauseAnalysis).HasColumnType("text");
                entity.Property(a => a.ContributingFactors).HasColumnType("text");
                entity.Property(a => a.StakeholdersAffected).HasColumnType("text");
                entity.Property(a => a.MeasuresTaken).HasColumnType("text");
                entity.Property(a => a.PreventionMeasures).HasColumnType("text");
                entity.Property(a => a.FollowUpActions).HasColumnType("text");
                entity.Property(a => a.InternalNotes).HasColumnType("text");
            });
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

            // ===== Sop Configuration =====
            modelBuilder.Entity<Sop>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Name).IsRequired().HasMaxLength(150);
                entity.Property(s => s.Description).HasMaxLength(1000);
            });

            // ===== SopViolationType Configuration =====
            modelBuilder.Entity<SopViolationType>(entity =>
            {
                entity.HasKey(sv => sv.Id);
                entity.Property(sv => sv.Name).IsRequired().HasMaxLength(150);
                entity.Property(sv => sv.ModelIdentifier).IsRequired().HasMaxLength(100);
                entity.Property(sv => sv.Description).HasMaxLength(1000);

                entity.HasOne(sv => sv.Sop)
                    .WithMany(s => s.ViolationTypes)
                    .HasForeignKey(sv => sv.SopId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== TenantViolationRequest Configuration =====
            modelBuilder.Entity<TenantViolationRequest>(entity =>
            {
                entity.HasKey(tr => tr.Id);
                
                entity.HasOne(tr => tr.Tenant)
                    .WithMany()
                    .HasForeignKey(tr => tr.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(tr => tr.SopViolationType)
                    .WithMany(sv => sv.TenantRequests)
                    .HasForeignKey(tr => tr.SopViolationTypeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== CameraViolationType Configuration (Many-to-Many) =====
            modelBuilder.Entity<CameraViolationType>(entity =>
            {
                entity.HasKey(cv => new { cv.CameraId, cv.SopViolationTypeId });

                entity.HasOne(cv => cv.Camera)
                    .WithMany(c => c.ActiveViolationTypes)
                    .HasForeignKey(cv => cv.CameraId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cv => cv.SopViolationType)
                    .WithMany(sv => sv.CameraViolations)
                    .HasForeignKey(cv => cv.SopViolationTypeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== Soft Delete Filters =====
            modelBuilder.Entity<Sop>().HasQueryFilter(s => !s.IsDeleted);
            modelBuilder.Entity<SopViolationType>().HasQueryFilter(sv => !sv.IsDeleted);
            modelBuilder.Entity<TenantViolationRequest>().HasQueryFilter(tr => !tr.IsDeleted);
            modelBuilder.Entity<Camera>().HasQueryFilter(c => !c.IsDeleted);
            modelBuilder.Entity<Tenant>().HasQueryFilter(t => !t.IsDeleted);
            modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    // Check if entity supports soft delete (has IsDeleted property)
                    var isDeletedProp = entry.Entity.GetType().GetProperty("IsDeleted");
                    var deletedAtProp = entry.Entity.GetType().GetProperty("DeletedAt");

                    if (isDeletedProp != null && isDeletedProp.PropertyType == typeof(bool))
                    {
                        entry.State = EntityState.Modified;
                        isDeletedProp.SetValue(entry.Entity, true);
                        deletedAtProp?.SetValue(entry.Entity, DateTime.UtcNow);
                    }
                }
            }
            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    var isDeletedProp = entry.Entity.GetType().GetProperty("IsDeleted");
                    var deletedAtProp = entry.Entity.GetType().GetProperty("DeletedAt");

                    if (isDeletedProp != null && isDeletedProp.PropertyType == typeof(bool))
                    {
                        entry.State = EntityState.Modified;
                        isDeletedProp.SetValue(entry.Entity, true);
                        deletedAtProp?.SetValue(entry.Entity, DateTime.UtcNow);
                    }
                }
            }
            return base.SaveChanges();
        }
    }
}