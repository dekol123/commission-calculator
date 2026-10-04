using Microsoft.Extensions.Options;

namespace Wallet.Infrastructure;

public sealed class PayoutOptions
{
    public const string SectionName = "Payout";

    public int IntervalSeconds { get; set; } = 60;

    public int MaxClaimFailures { get; set; } = 8;

    public int DrainLimit { get; set; } = 20;
}

public sealed class PayoutWorker(
    IServiceScopeFactory scopes,
    IOptions<PayoutOptions> options,
    ILogger<PayoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        var drainLimit = Math.Max(1, options.Value.DrainLimit);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                for (var index = 0; index < drainLimit && !stoppingToken.IsCancellationRequested; index++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<PayoutProcessor>();
                    var step = await processor.ProcessOnceAsync(CancellationToken.None);
                    if (step == PayoutStep.Idle)
                        break;
                }
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
