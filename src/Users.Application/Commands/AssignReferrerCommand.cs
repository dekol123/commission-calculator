using MediatR;

namespace Users.Application;

public sealed record AssignReferrerCommand(string ExternalId, string ReferrerExternalId) : IRequest<ReferralChange>;
