using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Users.Domain;

namespace Users.Infrastructure;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public Guid? ReferrerId { get; set; }
    public UserAccount? Referrer { get; set; }
}

public sealed class ExternalLevelRow
{
    public string ExternalId { get; set; } = "";
    public int Level { get; set; }
}

public sealed class UsersDb(DbContextOptions<UsersDb> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.ExternalId).HasMaxLength(ExternalIds.MaxLength).IsRequired();
            entity.HasIndex(user => user.ExternalId).IsUnique();
            entity.HasOne(user => user.Referrer)
                .WithMany()
                .HasForeignKey(user => user.ReferrerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed class UsersDbFactory : IDesignTimeDbContextFactory<UsersDb>
{
    public UsersDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UsersDb>()
            .UseNpgsql("Host=localhost;Port=5434;Database=users_db;Username=app;Password=app;Maximum Pool Size=20")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new UsersDb(options);
    }
}
