using MediatR;

namespace Wallet.Application;

public sealed class AcceptInboxHandler(IInbox inbox) : IRequestHandler<AcceptInboxCommand>
{
    public Task Handle(AcceptInboxCommand command, CancellationToken cancellationToken) =>
        inbox.AcceptAsync(command.Request, cancellationToken);
}