using Contracts;
using MediatR;

namespace Wallet.Application;

public sealed class GetPayoutHistoryHandler(IWalletQueries wallets) : IRequestHandler<GetPayoutHistoryQuery, IReadOnlyList<PayoutHistoryItemResponse>>
{
    public Task<IReadOnlyList<PayoutHistoryItemResponse>> Handle(GetPayoutHistoryQuery query, CancellationToken cancellationToken) =>
        wallets.HistoryAsync(query.ExternalId, cancellationToken);
}
