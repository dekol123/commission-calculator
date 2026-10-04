using MediatR;

namespace Users.Application;

public sealed class AssignReferrerHandler(IUserTree users) : IRequestHandler<AssignReferrerCommand, ReferralChange>
{
    public Task<ReferralChange> Handle(AssignReferrerCommand command, CancellationToken cancellationToken) =>
        users.AssignReferrerAsync(command.ExternalId, command.ReferrerExternalId, cancellationToken);
}
