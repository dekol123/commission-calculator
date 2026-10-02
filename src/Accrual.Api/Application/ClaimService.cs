using Accrual.Api.Domain;
using Contracts;

namespace Accrual.Api.Application;

public sealed class ClaimService(IClaimStore store, ILogger<ClaimService> logger)
{
    public async Task<ClaimPayoutResponse> ClaimAsync(ClaimPayoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commissionIds = request.CommissionIds ?? [];
        var claimed = await store.ClaimAsync(request.PayoutId, commissionIds, cancellationToken);

        logger.LogInformation(
            "Claimed payout {PayoutId} with {CommissionCount} commissions",
            request.PayoutId,
            claimed.Count);

        return new ClaimPayoutResponse(
            request.PayoutId,
            claimed.Select(commission => new ClaimedCommissionResponse(
                commission.CommissionId,
                commission.BeneficiaryExternalId,
                commission.Amount,
                commission.EventExternalId,
                commission.Level,
                commission.SchemaType)).ToList());
    }
}
