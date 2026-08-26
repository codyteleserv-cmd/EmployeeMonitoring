using EmployeeMonitoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.Data;

/// <summary>
/// Main monitoring database context.
/// </summary>
public class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();
    public DbSet<DlpEvent> DlpEvents => Set<DlpEvent>();
    public DbSet<PauseEvent> PauseEvents => Set<PauseEvent>();
    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();
    public DbSet<GlobalConfiguration> GlobalConfigurations => Set<GlobalConfiguration>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Agent
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastHeartbeat);
            
            entity.Property(e => e.AgentId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.OsVersion).HasMaxLength(100);
            entity.Property(e => e.AgentVersion).HasMaxLength(50);
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.Agents)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.Department)
                .WithMany(d => d.Agents)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.Team)
                .WithMany(t => t.Agents)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserSid).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.DepartmentId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => e.Role);
            
            entity.Property(e => e.UserSid).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(320).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
        });

        // Department
        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });

        // Team
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(e => e.Department)
                .WithMany(d => d.Teams)
                .HasForeignKey(e => e.DepartmentId);
        });

        // Screenshot
        modelBuilder.Entity<Screenshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.CapturedAt);
            entity.HasIndex(e => new { e.AgentId, e.CapturedAt });
            
            entity.Property(e => e.ImageData).HasColumnType("bytea");
            entity.Property(e => e.ThumbnailData).HasColumnType("bytea");
            entity.Property(e => e.Format).HasMaxLength(20);
            entity.Property(e => e.ActiveWindowTitle).HasMaxLength(500);
            entity.Property(e => e.ActiveProcessName).HasMaxLength(200);
            
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.Screenshots)
                .HasForeignKey(e => e.AgentId);
        });

        // ActivitySample
        modelBuilder.Entity<ActivitySample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.AgentId, e.Timestamp });
            
            entity.Property(e => e.ProcessName).HasMaxLength(200);
            entity.Property(e => e.WindowTitle).HasMaxLength(500);
            entity.Property(e => e.WindowClass).HasMaxLength(200);
            entity.Property(e => e.Domain).HasMaxLength(200);
            
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.ActivitySamples)
                .HasForeignKey(e => e.AgentId);
        });

        // DlpEvent
        modelBuilder.Entity<DlpEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.Acknowledged);
            entity.HasIndex(e => new { e.AgentId, e.Timestamp });
            
            entity.Property(e => e.ProcessName).HasMaxLength(200);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.Details).HasMaxLength(2000);
            
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.DlpEvents)
                .HasForeignKey(e => e.AgentId);
            
            entity.HasOne(e => e.AcknowledgedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // PauseEvent
        modelBuilder.Entity<PauseEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => new { e.AgentId, e.Timestamp });
            
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.AdminNotificationId).HasMaxLength(100);
            
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.PauseEvents)
                .HasForeignKey(e => e.AgentId);
        });

        // AgentConfiguration
        modelBuilder.Entity<AgentConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId).IsUnique();
            entity.Property(e => e.ConfigurationJson).HasColumnType("jsonb");
        });

        // GlobalConfiguration
        modelBuilder.Entity<GlobalConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Scope).IsUnique();
            entity.Property(e => e.Scope).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ConfigurationJson).HasColumnType("jsonb");
        });

        // ConsentRecord
        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.UserSid);
            entity.HasIndex(e => e.ConsentGiven);
            entity.Property(e => e.UserSid).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ConsentVersion).HasMaxLength(50).IsRequired();
            entity.Property(e => e.GrantedModulesJson).HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
        });

        // AlertRule
        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Enabled);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ConditionJson).HasColumnType("jsonb");
        });

        // Alert
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.Acknowledged);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            
            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasOne(e => e.AcknowledgedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}