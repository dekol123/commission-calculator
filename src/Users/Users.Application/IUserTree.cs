using Contracts;
using Users.Domain;

namespace Users.Application;

public abstract record ReferralChange
{
    public sealed record Updated : ReferralChange;
    public sealed record MissingUser : ReferralChange;
    public sealed record MissingReferrer : ReferralChange;
    public sealed record Rejected(ReferralViolation Violation) : ReferralChange;
}

public interface IUserTree
{
    Task<(UserResponse User, bool Created)> CreateAsync(string externalId, CancellationToken cancellationToken);

    Task<ReferralChange> AssignReferrerAsync(string externalId, string referrerExternalId, CancellationToken cancellationToken);

    Task<bool> ClearReferrerAsync(string externalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TreeNodeResponse>?> AncestorsAsync(string externalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TreeNodeResponse>?> DescendantsAsync(string externalId, CancellationToken cancellationToken);
}
