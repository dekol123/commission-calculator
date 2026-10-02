using System.Text.Json;
using Accrual.Api.Domain;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Accrual.Api.Data;

public sealed record PendingWork(
    Guid EventId,
    Guid LeaseToken,
    string ExternalId,
    string UserExternalId,
    decimal Profit);

public sealed class PendingCalculationStore(AccrualDb db, TimeProvider time)
{
    public async Task ReleaseStaleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var staleBefore = now.Subtract(TimeSpan.FromSeconds(30));
        var pending = ProfitEventStatus.Pending.ToString();
        var calculating = ProfitEventStatus.Calculating.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE profit_events
            SET status = {pending},
                lease_token = NULL,
                next_calculation_at = {now}
            WHERE status = {calculating}
              AND calculation_started_at IS NOT NULL
              AND calculation_started_at < {staleBefore}
            """,
            cancellationToken);
    }

    public async Task<PendingWork?> TakeOneAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var lease = Guid.NewGuid();
        var pending = ProfitEventStatus.Pending.ToString();
        var calculating = ProfitEventStatus.Calculating.ToString();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE profit_events AS evt
            SET status = {calculating},
                lease_token = {lease},
                calculation_started_at = {now},
                calculation_attempts = evt.calculation_attempts + 1
            WHERE evt.id = (
                SELECT candidate.id
                FROM profit_events AS candidate
                WHERE candidate.status = {pending}
                  AND candidate.next_calculation_at <= {now}
                ORDER BY candidate.created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            """,
            cancellationToken);

        if (updated == 0)
            return null;

        var ev = await db.Events.AsNoTracking().SingleAsync(item => item.LeaseToken == lease, cancellationToken);
        return new PendingWork(ev.Id, lease, ev.ExternalId, ev.UserExternalId, ev.Profit);
    }

    public Task<int> FinishAsync(PendingWork work, IReadOnlyList<string> ancestors, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var ev = await db.Events
                .Include(item => item.Commissions)
                .SingleOrDefaultAsync(
                    item => item.Id == work.EventId
                        && item.LeaseToken == work.LeaseToken
                        && item.Status == ProfitEventStatus.Calculating,
                    cancellationToken);
            if (ev is null)
                return -1;

            if (ev.Commissions.Count == 0)
            {
                var schema = await db.SchemaSettings.SingleAsync(
                    setting => setting.Id == SchemaSetting.SingletonId,
                    cancellationToken);
                var shares = CommissionCalculator.Calculate(ev.Profit, schema.SchemaType, ancestors);
                foreach (var share in shares)
                {
                    ev.Commissions.Add(new CommissionLine
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        Level = share.Level,
                        BeneficiaryExternalId = share.BeneficiaryExternalId,
                        Amount = share.Amount,
                        SchemaType = share.SchemaType
                    });
                }

                AddOutbox(ev, time.GetUtcNow());
            }

            ev.Status = ProfitEventStatus.Calculated;
            ev.LeaseToken = null;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ev.Commissions.Count;
        });
    }

    public async Task<bool> RejectAsync(PendingWork work, CancellationToken cancellationToken)
    {
        var ev = await db.Events.SingleOrDefaultAsync(
            item => item.Id == work.EventId && item.LeaseToken == work.LeaseToken,
            cancellationToken);
        if (ev is null)
            return false;

        ev.Status = ProfitEventStatus.Rejected;
        ev.LeaseToken = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task DeferAsync(PendingWork work, CancellationToken cancellationToken)
    {
        var ev = await db.Events.SingleOrDefaultAsync(
            item => item.Id == work.EventId && item.LeaseToken == work.LeaseToken,
            cancellationToken);
        if (ev is null)
            return;

        var seconds = Math.Min(60, Math.Pow(2, Math.Min(ev.CalculationAttempts, 6)));
        ev.Status = ProfitEventStatus.Pending;
        ev.LeaseToken = null;
        ev.NextCalculationAt = time.GetUtcNow().AddSeconds(seconds);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddOutbox(ProfitEvent profitEvent, DateTimeOffset now)
    {
        if (profitEvent.Commissions.Count == 0)
            return;

        var messageId = Guid.NewGuid();
        var payload = new WalletInboxRequest(
            messageId,
            profitEvent.Commissions.Select(line => new WalletInboxCommission(
                line.Id,
                line.BeneficiaryExternalId,
                line.Amount,
                profitEvent.ExternalId,
                line.Level,
                line.SchemaType)).ToList());

        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Payload = JsonSerializer.Serialize(payload, ApiJson.Options),
            Status = OutboxStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        });
    }
}
