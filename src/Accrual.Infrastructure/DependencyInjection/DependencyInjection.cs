using Accrual.Application;
using Accrual.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Accrual.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddAccrualInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddDbContext<AccrualDb>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
            options.UseSnakeCaseNamingConvention();
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccrualMetrics, AccrualMetrics>();
        services.AddScoped<IEventIntakeStore, EventIntakeStore>();
        services.AddScoped<IEventQueryStore, EventQueryStore>();
        services.AddScoped<IClaimStore, ClaimStore>();
        services.AddScoped<PendingCalculationStore>();
        services.AddScoped<OutboxStore>();
        services.AddScoped<ISchemaSettings, SchemaService>();
        services.AddScoped<EventIntakeService>();
        services.AddScoped<ClaimService>();
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        if (configuration.GetValue("Workers:Enabled", true))
        {
            services.AddHostedService<PendingCalculationWorker>();
            services.AddHostedService<OutboxDispatcher>();
        }

        services.AddTransient<CorrelationPropagationHandler>();
        var usersAddress = RequireBaseAddress(configuration["Users:BaseAddress"], "Users:BaseAddress");
        var walletAddress = RequireBaseAddress(configuration["Wallet:BaseAddress"], "Wallet:BaseAddress");

        services.AddHttpClient<IUsersGateway, UsersHttpGateway>(client => client.BaseAddress = usersAddress)
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IAccrualMetrics>(), "users"))
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<WalletHttpClient>(client => client.BaseAddress = walletAddress)
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IAccrualMetrics>(), "wallet"))
            .AddStandardResilienceHandler(ConfigureResilience);

        return services;
    }

    private static Uri RequireBaseAddress(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");

        if (!value.EndsWith('/'))
            value += "/";

        return new Uri(value);
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 2;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 4;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    }
}
