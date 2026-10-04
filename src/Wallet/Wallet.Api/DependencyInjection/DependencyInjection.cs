using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Application.DependencyInjection;
using Wallet.Infrastructure.DependencyInjection;

namespace Wallet.Api.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddWallet(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
            options.SwaggerDoc("v1", new() { Title = "Wallet", Version = "v1" }));
        services.AddWalletApplication();
        services.AddWalletInfrastructure(configuration);
        services.AddHealthChecks().AddNpgSql(connectionString, name: "database", tags: ["ready"]);
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
        return services;
    }
}
