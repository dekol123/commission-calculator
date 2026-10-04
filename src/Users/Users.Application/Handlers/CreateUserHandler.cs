using MediatR;

namespace Users.Application;

public sealed class CreateUserHandler(IUserTree users) : IRequestHandler<CreateUserCommand, CreateUserResult>
{
    public async Task<CreateUserResult> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var (user, created) = await users.CreateAsync(command.ExternalId, cancellationToken);
        return new CreateUserResult(user, created);
    }
}
