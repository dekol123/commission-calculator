using Contracts;
using MediatR;

namespace Users.Application;

public sealed class GetDescendantsHandler(IUserTree users) : IRequestHandler<GetDescendantsQuery, IReadOnlyList<TreeNodeResponse>?>
{
    public Task<IReadOnlyList<TreeNodeResponse>?> Handle(GetDescendantsQuery query, CancellationToken cancellationToken) =>
        users.DescendantsAsync(query.ExternalId, cancellationToken);
}