using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class WalletDb(DbContextOptions<WalletDb> options) : DbContext(options)
{
    public DbSet<WalletAccount> Wallets => Set<WalletAccount>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();
    public DbSet<PayoutRecord> Payouts => Set<PayoutRecord>();
    public DbSet<PayoutLine> PayoutLines => Set<PayoutLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletAccount>(entity =>
        {
            entity.ToTable("wallet_accounts");
            entity.HasKey(wallet => wallet.Id);
            entity.Property(wallet => wallet.ExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.HasIndex(wallet => wallet.ExternalId).IsUnique();
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(message => message.MessageId).IsUnique();
            entity.HasIndex(message => message.Status);
            entity.HasIndex(message => message.PayoutId);
        });

        modelBuilder.Entity<PayoutBatch>(entity =>
        {
            entity.ToTable("payout_batches");
            entity.HasKey(batch => batch.PayoutId);
            entity.HasIndex(batch => new { batch.LeaseToken, batch.NextAttemptAt });
        });

        modelBuilder.Entity<PayoutRecord>(entity =>
        {
            entity.ToTable("payouts");
            entity.HasKey(payout => payout.Id);
            entity.Property(payout => payout.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<PayoutLine>(entity =>
        {
            entity.ToTable("payout_lines");
            entity.HasKey(line => line.Id);
            entity.Property(line => line.BeneficiaryExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.Property(line => line.EventExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.Property(line => line.Amount).HasPrecision(19, 2);
            entity.Property(line => line.SchemaType).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(line => line.CommissionId).IsUnique();
            entity.HasIndex(line => line.BeneficiaryExternalId);
        });
    }
}

public sealed class WalletDbFactory : IDesignTimeDbContextFactory<WalletDb>
{
    public WalletDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletDb>()
            .UseNpgsql("Host=localhost;Port=5436;Database=wallet_db;Username=app;Password=app;Maximum Pool Size=20")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new WalletDb(options);
    }
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
