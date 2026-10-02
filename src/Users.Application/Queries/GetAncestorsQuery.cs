using Contracts;
using MediatR;

namespace Users.Application;

public sealed record GetAncestorsQuery(string ExternalId) : IRequest<IReadOnlyList<TreeNodeResponse>?>;
