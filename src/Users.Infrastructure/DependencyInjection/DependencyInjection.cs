using Microsoft.EntityFrameworkCore;
using Users.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Users.Application;

namespace Users.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddDbContext<UsersDb>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
            options.UseSnakeCaseNamingConvention();
        });
        services.AddScoped<IUserTree, UserTreeService>();
        return services;
    }
}
