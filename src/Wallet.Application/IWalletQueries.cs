using Contracts;

namespace Wallet.Application;

public interface IWalletQueries
{
    Task<WalletBalanceResponse> GetBalanceAsync(string externalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayoutHistoryItemResponse>> HistoryAsync(string externalId, CancellationToken cancellationToken);
}
