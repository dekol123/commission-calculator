using Accrual.Api.Application;
using Accrual.Api.Data;
using Accrual.Api.Observability;
using Microsoft.Extensions.Options;

namespace Accrual.Api.Workers;

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    public int CalculationIntervalSeconds { get; set; } = 5;

    public int OutboxIntervalSeconds { get; set; } = 2;

    public int OutboxMaxAttempts { get; set; } = 8;
}

public sealed class PendingCalculationWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    ILogger<PendingCalculationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.CalculationIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pending calculation iteration failed");
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

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        for (var index = 0; index < 20 && !stoppingToken.IsCancellationRequested; index++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<PendingCalculationStore>();
            var users = scope.ServiceProvider.GetRequiredService<IUsersGateway>();
            var metrics = scope.ServiceProvider.GetRequiredService<IAccrualMetrics>();
            if (index == 0)
                await store.ReleaseStaleAsync(CancellationToken.None);

            var work = await store.TakeOneAsync(CancellationToken.None);
            if (work is null)
                return;

            var correlationId = Guid.NewGuid().ToString("n");
            CorrelationContext.Current = correlationId;
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["EventExternalId"] = work.ExternalId
            });

            var lookup = await users.GetAncestorsAsync(work.UserExternalId, CancellationToken.None);
            switch (lookup)
            {
                case AncestorLookup.NotFound:
                    await store.RejectAsync(work, CancellationToken.None);
                    logger.LogInformation(
                        "Pending event {EventExternalId} rejected because user {UserExternalId} does not exist",
                        work.ExternalId,
                        work.UserExternalId);
                    break;
                case AncestorLookup.Found found:
                    var commissionCount = await store.FinishAsync(work, found.AncestorsNearestFirst, CancellationToken.None);
                    if (commissionCount >= 0)
                    {
                        if (commissionCount > 0)
                            metrics.CommissionsCalculated(commissionCount);
                        logger.LogInformation(
                            "Calculated pending event {EventExternalId} with {CommissionCount} commissions",
                            work.ExternalId,
                            commissionCount);
                    }
                    break;
                default:
                    await store.DeferAsync(work, CancellationToken.None);
                    logger.LogWarning("Deferred pending event {EventExternalId} because Users is unavailable", work.ExternalId);
                    break;
            }
        }
    }
}
