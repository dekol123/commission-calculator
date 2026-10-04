using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class InboxService(WalletDb db, TimeProvider time, ILogger<InboxService> logger) : IInbox
{
    public async Task AcceptAsync(WalletInboxRequest request, CancellationToken cancellationToken)
    {
        if (await db.Inbox.AnyAsync(message => message.MessageId == request.MessageId, cancellationToken))
            return;

        db.Inbox.Add(new InboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = request.MessageId,
            Payload = JsonSerializer.Serialize(request, ApiJson.Options),
            Status = InboxStatus.Received,
            ReceivedAt = time.GetUtcNow()
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Stored inbox message {MessageId} with {CommissionCount} commissions",
                request.MessageId,
                request.Commissions.Count);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
        }
    }
}

public sealed class WalletQueryService(WalletDb db, TimeProvider time) : IWalletQueries
{
    public async Task<WalletBalanceResponse> GetBalanceAsync(string externalId, CancellationToken cancellationToken)
    {
        await EnsureWalletAsync(externalId, cancellationToken);
        var balance = await db.PayoutLines
            .Where(line => line.BeneficiaryExternalId == externalId)
            .SumAsync(line => (decimal?)line.Amount, cancellationToken) ?? 0m;
        return new WalletBalanceResponse(externalId, balance);
    }

    public async Task<IReadOnlyList<PayoutHistoryItemResponse>> HistoryAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        return await db.PayoutLines.AsNoTracking()
            .Where(line => line.BeneficiaryExternalId == externalId)
            .OrderByDescending(line => line.PaidAt)
            .Select(line => new PayoutHistoryItemResponse(
                line.PayoutId,
                line.CommissionId,
                line.EventExternalId,
                line.Level,
                line.Amount,
                line.SchemaType,
                line.PaidAt))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureWalletAsync(string externalId, CancellationToken cancellationToken)
    {
        if (await db.Wallets.AnyAsync(wallet => wallet.ExternalId == externalId, cancellationToken))
            return;

        var now = time.GetUtcNow();
        var id = Guid.NewGuid();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO wallet_accounts (id, external_id, created_at)
                VALUES ({id}, {externalId}, {now})
                ON CONFLICT (external_id) DO NOTHING
                """,
                cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
        {
        }
    }
}
