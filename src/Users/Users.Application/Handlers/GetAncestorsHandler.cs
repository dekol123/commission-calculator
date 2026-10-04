using Contracts;
using MediatR;

namespace Users.Application;

public sealed class GetAncestorsHandler(IUserTree users) : IRequestHandler<GetAncestorsQuery, IReadOnlyList<TreeNodeResponse>?>
{
    public Task<IReadOnlyList<TreeNodeResponse>?> Handle(GetAncestorsQuery query, CancellationToken cancellationToken) =>
        users.AncestorsAsync(query.ExternalId, cancellationToken);
}
