using Accrual.Api.Application;
using Accrual.Api.Clients;
using Accrual.Api.Data;
using Accrual.Api.Observability;
using Microsoft.Extensions.Options;

namespace Accrual.Api.Workers;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.OutboxIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox iteration failed");
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

    private async Task DispatchOneAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
            return;

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletHttpClient>();
        var metrics = scope.ServiceProvider.GetRequiredService<IAccrualMetrics>();
        await store.ReleaseStaleAsync(CancellationToken.None);
        metrics.SetOutboxDepth(await store.DepthAsync(CancellationToken.None));

        var work = await store.TakeOneAsync(CancellationToken.None);
        if (work is null)
            return;

        var correlationId = Guid.NewGuid().ToString("n");
        CorrelationContext.Current = correlationId;
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["MessageId"] = work.MessageId
        });

        try
        {
            await wallet.SendAsync(work.Payload, CancellationToken.None);
            await store.MarkDeliveredAsync(work, CancellationToken.None);
            logger.LogInformation("Delivered outbox message {MessageId}", work.MessageId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Outbox message {MessageId} delivery failed on attempt {Attempt}", work.MessageId, work.Attempts);
            await store.MarkFailedAsync(work, exception.Message, options.Value.OutboxMaxAttempts, CancellationToken.None);
            if (work.Attempts >= options.Value.OutboxMaxAttempts)
            {
                metrics.OutboxFailed();
                logger.LogError("Outbox message {MessageId} exhausted retries", work.MessageId);
            }
        }

        metrics.SetOutboxDepth(await store.DepthAsync(CancellationToken.None));
    }
}
