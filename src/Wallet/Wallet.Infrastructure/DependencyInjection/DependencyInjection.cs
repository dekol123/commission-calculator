using Microsoft.EntityFrameworkCore;
using Wallet.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Wallet.Application;

namespace Wallet.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddWalletInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddDbContext<WalletDb>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
            options.UseSnakeCaseNamingConvention();
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWalletMetrics, WalletMetrics>();
        services.AddScoped<IInbox, InboxService>();
        services.AddScoped<IWalletQueries, WalletQueryService>();
        services.AddScoped<PayoutProcessor>();
        services.Configure<PayoutOptions>(configuration.GetSection(PayoutOptions.SectionName));
        if (configuration.GetValue("Workers:Enabled", true))
            services.AddHostedService<PayoutWorker>();

        services.AddTransient<CorrelationPropagationHandler>();
        var accrualAddress = configuration["Accrual:BaseAddress"];
        if (string.IsNullOrWhiteSpace(accrualAddress))
            throw new InvalidOperationException("Accrual:BaseAddress is required.");
        if (!accrualAddress.EndsWith('/'))
            accrualAddress += "/";

        services.AddHttpClient<IAccrualClient, AccrualHttpClient>(client => client.BaseAddress = new Uri(accrualAddress))
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IWalletMetrics>(), "accrual"))
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 4;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });

        return services;
    }
}
