using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Users.Application.DependencyInjection;
using Users.Infrastructure.DependencyInjection;

namespace Users.Api.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddUsers(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
            options.SwaggerDoc("v1", new() { Title = "Users", Version = "v1" }));
        services.AddUsersApplication();
        services.AddUsersInfrastructure(configuration);
        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "database", tags: ["ready"]);
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
        return services;
    }
}
