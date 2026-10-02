using MediatR;

namespace Users.Application;

public sealed record CreateUserCommand(string ExternalId) : IRequest<CreateUserResult>;
