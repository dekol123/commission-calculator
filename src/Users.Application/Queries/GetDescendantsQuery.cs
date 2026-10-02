using Contracts;
using MediatR;

namespace Users.Application;

public sealed record GetDescendantsQuery(string ExternalId) : IRequest<IReadOnlyList<TreeNodeResponse>?>;
