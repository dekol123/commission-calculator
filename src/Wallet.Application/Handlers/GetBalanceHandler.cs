using Contracts;
using MediatR;

namespace Wallet.Application;

public sealed class GetBalanceHandler(IWalletQueries wallets) : IRequestHandler<GetBalanceQuery, WalletBalanceResponse>
{
    public Task<WalletBalanceResponse> Handle(GetBalanceQuery query, CancellationToken cancellationToken) =>
        wallets.GetBalanceAsync(query.ExternalId, cancellationToken);
}
