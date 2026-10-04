using Contracts;

namespace Wallet.Application;

public interface IInbox
{
    Task AcceptAsync(WalletInboxRequest request, CancellationToken cancellationToken);
}
