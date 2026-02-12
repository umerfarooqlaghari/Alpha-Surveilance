using Microsoft.EntityFrameworkCore;
using audit_api.Core.Domain;

namespace audit_api.Data
{
    public class AuditDbContext : DbContext
    {
        public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure AuditLog table
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(e => e.Id);
                
                // TimescaleDB optimization: Index the timestamp
                entity.HasIndex(e => e.Timestamp);
                
                // Multi-tenancy optimization
                entity.HasIndex(e => e.TenantId);
            });
        }
    }
}
