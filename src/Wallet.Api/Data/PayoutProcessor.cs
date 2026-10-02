using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Wallet.Api.Clients;

namespace Wallet.Api.Data;

public sealed class PayoutProcessor(
    WalletDb db,
    TimeProvider time,
    IAccrualClient accrual,
    IWalletMetrics metrics,
    ILogger<PayoutProcessor> logger)
{
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var claimingPayoutId = await db.Inbox.AsNoTracking()
            .Where(message => message.Status == InboxStatus.Claiming && message.PayoutId != null)
            .OrderBy(message => message.ReceivedAt)
            .Select(message => message.PayoutId)
            .FirstOrDefaultAsync(cancellationToken);

        if (claimingPayoutId is Guid existingPayoutId)
            return await FinishAsync(existingPayoutId, cancellationToken);

        var payoutId = Guid.NewGuid();
        var taken = await TakeReceivedAsync(payoutId, cancellationToken);
        if (taken == 0)
            return false;

        return await FinishAsync(payoutId, cancellationToken);
    }

    private async Task<int> TakeReceivedAsync(Guid payoutId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var received = InboxStatus.Received.ToString();
        var claiming = InboxStatus.Claiming.ToString();
        return await db.Database.ExecuteSqlInterpolatedAsync(
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
    }

    private async Task<bool> FinishAsync(Guid payoutId, CancellationToken cancellationToken)
    {
        var messages = await db.Inbox.AsNoTracking()
            .Where(message => message.PayoutId == payoutId && message.Status == InboxStatus.Claiming)
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
            return false;

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
            logger.LogWarning("Accrual claim for payout {PayoutId} is unavailable and will be retried", payoutId);
            return true;
        }

        var booked = await BookAsync(payoutId, claim.Commissions, cancellationToken);
        if (booked > 0)
            metrics.PayoutBooked(booked);

        logger.LogInformation("Booked payout {PayoutId} for amount {Amount}", payoutId, booked);
        return true;
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
                db.Payouts.Add(new PayoutRecord
                {
                    Id = payoutId,
                    Status = PayoutStatus.Completed,
                    CreatedAt = now
                });
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
                db.PayoutLines.Add(new PayoutLine
                {
                    Id = Guid.NewGuid(),
                    PayoutId = payoutId,
                    CommissionId = commission.CommissionId,
                    BeneficiaryExternalId = commission.BeneficiaryExternalId,
                    Amount = commission.Amount,
                    EventExternalId = commission.EventExternalId,
                    Level = commission.Level,
                    SchemaType = commission.SchemaType,
                    PaidAt = now
                });
                added += commission.Amount;
            }

            var claiming = await db.Inbox
                .Where(message => message.PayoutId == payoutId && message.Status == InboxStatus.Claiming)
                .ToListAsync(cancellationToken);
            foreach (var message in claiming)
                message.Status = InboxStatus.Completed;

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

        db.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            CreatedAt = now
        });
    }
}
