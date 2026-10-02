using MediatR;

namespace Users.Application;

public sealed record ClearReferrerCommand(string ExternalId) : IRequest<bool>;
