using System.Data;
using Accrual.Api.Application;
using Accrual.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accrual.Api.Data;

public sealed class ClaimStore(AccrualDb db) : IClaimStore
{
    public async Task<IReadOnlyList<ClaimedCommission>> ClaimAsync(
        Guid payoutId,
        IReadOnlyList<Guid> commissionIds,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var ids = commissionIds.Distinct().ToArray();
            var rows = await db.Commissions
                .Include(commission => commission.Event)
                .Where(commission => commission.PayoutId == payoutId || ids.Contains(commission.Id))
                .ToListAsync(cancellationToken);

            var claimed = ClaimTransition.Apply(rows, payoutId, ids);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (IReadOnlyList<ClaimedCommission>)claimed
                .OrderBy(commission => commission.Event.ExternalId)
                .ThenBy(commission => commission.Level)
                .Select(commission => new ClaimedCommission(
                    commission.Id,
                    commission.BeneficiaryExternalId,
                    commission.Amount,
                    commission.Event.ExternalId,
                    commission.Level,
                    commission.SchemaType))
                .ToList();
        });
    }
}
