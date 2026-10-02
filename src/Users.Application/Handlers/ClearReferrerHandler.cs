using MediatR;

namespace Users.Application;

public sealed class ClearReferrerHandler(IUserTree users) : IRequestHandler<ClearReferrerCommand, bool>
{
    public Task<bool> Handle(ClearReferrerCommand command, CancellationToken cancellationToken) =>
        users.ClearReferrerAsync(command.ExternalId, cancellationToken);
}
