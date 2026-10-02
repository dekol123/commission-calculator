using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed class ClaimPayoutHandler(ClaimService claims) : IRequestHandler<ClaimPayoutCommand, ClaimPayoutResponse>
{
    public Task<ClaimPayoutResponse> Handle(ClaimPayoutCommand command, CancellationToken cancellationToken) =>
        claims.ClaimAsync(command.Request, cancellationToken);
}
