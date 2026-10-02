using Contracts;

namespace Users.Application;

public sealed record CreateUserResult(UserResponse User, bool Created);
