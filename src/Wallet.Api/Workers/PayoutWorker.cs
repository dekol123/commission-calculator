using Microsoft.Extensions.Options;
using Wallet.Api.Clients;
using Wallet.Api.Data;

namespace Wallet.Api.Workers;

public sealed class PayoutOptions
{
    public const string SectionName = "Payout";

    public int IntervalSeconds { get; set; } = 60;
}

public sealed class PayoutWorker(
    IServiceScopeFactory scopes,
    IOptions<PayoutOptions> options,
    ILogger<PayoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<PayoutProcessor>();
                if (!stoppingToken.IsCancellationRequested)
                    await processor.ProcessOnceAsync(CancellationToken.None);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Payout iteration failed");
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
