using Accrual.Domain;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accrual.Infrastructure;

public sealed class ProfitEvent
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string UserExternalId { get; set; } = "";
    public decimal Profit { get; set; }
    public ProfitEventStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextCalculationAt { get; set; }
    public int CalculationAttempts { get; set; }
    public DateTimeOffset? CalculationStartedAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public List<CommissionLine> Commissions { get; set; } = [];
}

public sealed class CommissionLine : IClaimableCommission
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public ProfitEvent Event { get; set; } = null!;
    public int Level { get; set; }
    public string BeneficiaryExternalId { get; set; } = "";
    public decimal Amount { get; set; }
    public SchemaType SchemaType { get; set; }
    public bool IsPaid { get; set; }
    public Guid? PayoutId { get; set; }
}

public enum OutboxStatus
{
    Pending,
    Dispatching,
    Delivered,
    Failed
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Payload { get; set; } = "";
    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public Guid? DispatchToken { get; set; }
    public string? LastError { get; set; }
}

public sealed class SchemaSetting
{
    public const int SingletonId = 1;

    public int Id { get; set; }
    public SchemaType SchemaType { get; set; }
}

public sealed class AccrualDb(DbContextOptions<AccrualDb> options) : DbContext(options)
{
    public DbSet<ProfitEvent> Events => Set<ProfitEvent>();
    public DbSet<CommissionLine> Commissions => Set<CommissionLine>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<SchemaSetting> SchemaSettings => Set<SchemaSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfitEvent>(entity =>
        {
            entity.ToTable("profit_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.Property(e => e.UserExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.Property(e => e.Profit).HasPrecision(19, 4);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextCalculationAt });
        });

        modelBuilder.Entity<CommissionLine>(entity =>
        {
            entity.ToTable("commissions");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.BeneficiaryExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.Property(c => c.Amount).HasPrecision(19, 2);
            entity.Property(c => c.SchemaType).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(c => new { c.EventId, c.Level }).IsUnique();
            entity.HasIndex(c => c.PayoutId);
            entity.HasOne(c => c.Event)
                .WithMany(e => e.Commissions)
                .HasForeignKey(c => c.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Payload).IsRequired();
            entity.Property(m => m.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(m => m.LastError).HasMaxLength(2000);
            entity.HasIndex(m => m.MessageId).IsUnique();
            entity.HasIndex(m => new { m.Status, m.NextAttemptAt });
        });

        modelBuilder.Entity<SchemaSetting>(entity =>
        {
            entity.ToTable("schema_settings");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.SchemaType).HasConversion<string>().HasMaxLength(32);
            entity.HasData(new SchemaSetting { Id = SchemaSetting.SingletonId, SchemaType = SchemaType.Fibonacci });
        });
    }
}

public sealed class AccrualDbFactory : IDesignTimeDbContextFactory<AccrualDb>
{
    public AccrualDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccrualDb>()
            .UseNpgsql("Host=localhost;Port=5435;Database=accrual_db;Username=app;Password=app;Maximum Pool Size=20")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AccrualDb(options);
    }
}

internal static class EventMaps
{
    public static StoredEvent ToStored(ProfitEvent ev) =>
        new(
            ev.Id,
            ev.ExternalId,
            ev.UserExternalId,
            ev.Profit,
            ev.Status,
            ev.CreatedAt,
            ev.Commissions
                .OrderBy(commission => commission.Level)
                .Select(commission => new StoredCommission(
                    commission.Id,
                    commission.BeneficiaryExternalId,
                    commission.Level,
                    commission.Amount,
                    commission.SchemaType,
                    commission.IsPaid,
                    commission.PayoutId))
                .ToList());
}

internal static class PostgresErrors
{
    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }
}
