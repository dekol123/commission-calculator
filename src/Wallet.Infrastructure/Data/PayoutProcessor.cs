using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class PayoutProcessor(
    WalletDb db,
    TimeProvider time,
    IAccrualClient accrual,
    IWalletMetrics metrics,
    IOptions<PayoutOptions> options,
    ILogger<PayoutProcessor> logger)
{
    public async Task<PayoutStep> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await ReleaseStaleLeasesAsync(cancellationToken);

        var token = Guid.NewGuid();
        var adopted = await AdoptBatchAsync(token, cancellationToken);
        if (adopted is Guid adoptedPayoutId)
            return await FinishAsync(adoptedPayoutId, token, cancellationToken);

        var payoutId = Guid.NewGuid();
        var taken = await TakeReceivedAsync(payoutId, token, cancellationToken);
        if (taken == 0)
            return PayoutStep.Idle;

        return await FinishAsync(payoutId, token, cancellationToken);
    }

    private async Task ReleaseStaleLeasesAsync(CancellationToken cancellationToken)
    {
        var staleBefore = time.GetUtcNow().Subtract(TimeSpan.FromSeconds(90));
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE payout_batches
            SET lease_token = NULL
            WHERE lease_token IS NOT NULL
              AND locked_at IS NOT NULL
              AND locked_at < {staleBefore}
            """,
            cancellationToken);
    }

    private async Task<Guid?> AdoptBatchAsync(Guid token, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE payout_batches AS batch
            SET lease_token = {token},
                locked_at = {now}
            WHERE batch.payout_id = (
                SELECT candidate.payout_id
                FROM payout_batches AS candidate
                WHERE candidate.lease_token IS NULL
                  AND candidate.next_attempt_at <= {now}
                ORDER BY candidate.next_attempt_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            """,
            cancellationToken);
        if (updated == 0)
            return null;

        return await db.PayoutBatches.AsNoTracking()
            .Where(batch => batch.LeaseToken == token)
            .Select(batch => (Guid?)batch.PayoutId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<int> TakeReceivedAsync(Guid payoutId, Guid token, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        db.ChangeTracker.Clear();
        var now = time.GetUtcNow();
        var received = InboxStatus.Received.ToString();
        var claiming = InboxStatus.Claiming.ToString();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var taken = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE inbox_messages AS message
            SET status = {claiming},
                payout_id = {payoutId},
                locked_at = {now}
            WHERE message.id IN (
                SELECT candidate.id
                FROM inbox_messages AS candidate
                WHERE candidate.status = {received}
                ORDER BY candidate.received_at
                FOR UPDATE SKIP LOCKED
                LIMIT 20
            )
            """,
            cancellationToken);
        if (taken == 0)
            return 0;

        db.PayoutBatches.Add(PayoutBatch.Lease(payoutId, token, now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return taken;
        });
    }

    private async Task<PayoutStep> FinishAsync(Guid payoutId, Guid token, CancellationToken cancellationToken)
    {
        var ownsLease = await db.PayoutBatches.AsNoTracking()
            .AnyAsync(batch => batch.PayoutId == payoutId && batch.LeaseToken == token, cancellationToken);
        if (!ownsLease)
            return PayoutStep.Idle;

        var messages = await db.Inbox.AsNoTracking()
            .Where(message => message.PayoutId == payoutId && message.Status == InboxStatus.Claiming)
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            await RemoveBatchAsync(payoutId, cancellationToken);
            return PayoutStep.Progress;
        }

        var commissions = new List<WalletInboxCommission>();
        var poisonIds = new List<Guid>();
        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<WalletInboxRequest>(message.Payload, ApiJson.Options);
                if (payload?.Commissions is null)
                    poisonIds.Add(message.Id);
                else
                    commissions.AddRange(payload.Commissions);
            }
            catch (JsonException)
            {
                poisonIds.Add(message.Id);
            }
        }

        if (poisonIds.Count > 0)
        {
            var poison = await db.Inbox
                .Where(message => poisonIds.Contains(message.Id) && message.Status == InboxStatus.Claiming)
                .ToListAsync(cancellationToken);
            foreach (var message in poison)
                message.Status = InboxStatus.Poison;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Inbox messages {MessageIds} for payout {PayoutId} are poison", poisonIds, payoutId);
        }

        var claimIds = commissions.Select(commission => commission.CommissionId).Distinct().ToArray();
        var claim = await accrual.ClaimAsync(new ClaimPayoutRequest(payoutId, claimIds), CancellationToken.None);
        if (claim is null)
        {
            await DeferBatchAsync(payoutId, token, cancellationToken);
            logger.LogWarning("Accrual claim for payout {PayoutId} is unavailable and will be retried", payoutId);
            return PayoutStep.Deferred;
        }

        var booked = await BookAsync(payoutId, claim.Commissions, cancellationToken);
        if (booked > 0)
            metrics.PayoutBooked(booked);

        logger.LogInformation("Booked payout {PayoutId} for amount {Amount}", payoutId, booked);
        return PayoutStep.Progress;
    }

    private async Task DeferBatchAsync(Guid payoutId, Guid token, CancellationToken cancellationToken)
    {
        var batch = await db.PayoutBatches.SingleOrDefaultAsync(
            item => item.PayoutId == payoutId && item.LeaseToken == token,
            cancellationToken);
        if (batch is null)
            return;

        batch.ClaimFailures++;
        batch.LeaseToken = null;
        batch.LockedAt = null;
        if (batch.ClaimFailures >= Math.Max(1, options.Value.MaxClaimFailures))
        {
            var claiming = await db.Inbox
                .Where(message => message.PayoutId == payoutId && message.Status == InboxStatus.Claiming)
                .ToListAsync(cancellationToken);
            foreach (var message in claiming)
                message.Status = InboxStatus.Failed;
            db.PayoutBatches.Remove(batch);
            logger.LogError(
                "Payout {PayoutId} exhausted claim retries after {Attempts} failures",
                payoutId,
                batch.ClaimFailures);
        }
        else
        {
            var seconds = Math.Min(60, Math.Pow(2, Math.Min(batch.ClaimFailures, 6)));
            batch.NextAttemptAt = time.GetUtcNow().AddSeconds(seconds);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RemoveBatchAsync(Guid payoutId, CancellationToken cancellationToken)
    {
        await db.PayoutBatches.Where(batch => batch.PayoutId == payoutId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<decimal> BookAsync(
        Guid payoutId,
        IReadOnlyList<ClaimedCommissionResponse> claimed,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = time.GetUtcNow();
            if (!await db.Payouts.AnyAsync(payout => payout.Id == payoutId, cancellationToken))
            {
                db.Payouts.Add(PayoutRecord.Completed(payoutId, now));
            }

            var ids = claimed.Select(commission => commission.CommissionId).ToArray();
            var existing = await db.PayoutLines
                .Where(line => ids.Contains(line.CommissionId))
                .Select(line => line.CommissionId)
                .ToListAsync(cancellationToken);

            decimal added = 0;
            foreach (var commission in claimed.Where(item => !existing.Contains(item.CommissionId)))
            {
                await EnsureWalletAsync(commission.BeneficiaryExternalId, now, cancellationToken);
                db.PayoutLines.Add(PayoutLine.Paid(payoutId, commission, now));
                added += commission.Amount;
            }

            var claiming = await db.Inbox
                .Where(message => message.PayoutId == payoutId && message.Status == InboxStatus.Claiming)
                .ToListAsync(cancellationToken);
            foreach (var message in claiming)
                message.Status = InboxStatus.Completed;

            var batch = await db.PayoutBatches.SingleOrDefaultAsync(item => item.PayoutId == payoutId, cancellationToken);
            if (batch is not null)
                db.PayoutBatches.Remove(batch);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return added;
        });
    }

    private async Task EnsureWalletAsync(string externalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Wallets.Local.Any(wallet => wallet.ExternalId == externalId))
            return;

        if (await db.Wallets.AnyAsync(wallet => wallet.ExternalId == externalId, cancellationToken))
            return;

        db.Wallets.Add(WalletAccount.Open(externalId, now));
    }

}
