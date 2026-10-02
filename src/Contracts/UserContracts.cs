namespace Contracts;

public sealed record CreateUserRequest(string ExternalId);

public sealed record UserResponse(string ExternalId);

public sealed record SetReferrerRequest(string ReferrerExternalId);

public sealed record TreeNodeResponse(string ExternalId, int Level);
