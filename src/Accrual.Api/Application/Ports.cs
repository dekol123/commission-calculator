using Accrual.Api.Domain;

namespace Accrual.Api.Application;

public interface IEventIntakeStore
{
    Task<StoredEvent?> FindAsync(string externalId, CancellationToken cancellationToken);

    Task<PersistOutcome> SavePendingAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        CancellationToken cancellationToken);

    Task<PersistOutcome> SaveCalculatedAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        IReadOnlyList<string> ancestorsNearestFirst,
        CancellationToken cancellationToken);
}

public interface IClaimStore
{
    Task<IReadOnlyList<ClaimedCommission>> ClaimAsync(
        Guid payoutId,
        IReadOnlyList<Guid> commissionIds,
        CancellationToken cancellationToken);
}

public interface IEventQueryStore
{
    Task<IReadOnlyList<StoredEvent>> ListByUserAsync(string userExternalId, CancellationToken cancellationToken);

    Task<StoredEvent?> FindAsync(string externalId, CancellationToken cancellationToken);
}

public abstract record AncestorLookup
{
    public sealed record Found(IReadOnlyList<string> AncestorsNearestFirst) : AncestorLookup;

    public sealed record NotFound : AncestorLookup;

    public sealed record CircuitOpen : AncestorLookup;

    public sealed record Unavailable : AncestorLookup;
}

public interface IUsersGateway
{
    Task<AncestorLookup> GetAncestorsAsync(string userExternalId, CancellationToken cancellationToken);
}

public interface IAccrualMetrics
{
    void EventAccepted(string status);

    void CommissionsCalculated(int count);

    void HttpClientFailed(string client);

    void OutboxFailed();

    void SetOutboxDepth(int depth);
}
