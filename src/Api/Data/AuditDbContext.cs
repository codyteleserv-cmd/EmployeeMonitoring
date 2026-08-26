using EmployeeMonitoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.Data;

/// <summary>
/// Audit log database context (separate for compliance/immutability).
/// </summary>
public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ActorId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.TargetType);
            entity.HasIndex(e => e.TargetId);
            entity.HasIndex(e => new { e.Timestamp, e.ActorId });
            
            entity.Property(e => e.ActorId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ActorRole).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TargetId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetName).HasMaxLength(200);
            entity.Property(e => e.Details).HasMaxLength(4000);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            
            // Make audit logs immutable (no update/delete in normal operations)
            // This is enforced at application level
        });
    }

    // Override SaveChanges to prevent modification of audit logs
    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit logs are immutable and cannot be modified or deleted.");
            }
        }
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit logs are immutable and cannot be modified or deleted.");
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}