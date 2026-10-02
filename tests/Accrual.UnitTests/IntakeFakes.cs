using Accrual.Application;
using Accrual.Domain;
using Contracts;

namespace Accrual.UnitTests;

internal sealed class InMemoryEventIntakeStore : IEventIntakeStore
{
    private readonly Dictionary<string, StoredEvent> _events = new(StringComparer.Ordinal);

    public SchemaType Schema { get; set; } = SchemaType.Linear;

    public int OutboxCount { get; private set; }

    public Task<StoredEvent?> FindAsync(string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(_events.TryGetValue(externalId, out var found) ? found : null);

    public Task<PersistOutcome> SavePendingAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        CancellationToken cancellationToken) =>
        Task.FromResult(Save(externalId, userExternalId, profit, ancestors: null));

    public Task<PersistOutcome> SaveCalculatedAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        IReadOnlyList<string> ancestorsNearestFirst,
        CancellationToken cancellationToken) =>
        Task.FromResult(Save(externalId, userExternalId, profit, ancestorsNearestFirst));

    private PersistOutcome Save(
        string externalId,
        string userExternalId,
        decimal profit,
        IReadOnlyList<string>? ancestors)
    {
        if (_events.TryGetValue(externalId, out var existing))
        {
            if (existing.UserExternalId != userExternalId || existing.Profit != profit)
                return new PersistOutcome.Conflict(existing);

            return new PersistOutcome.Stored(existing, false);
        }

        var commissions = ancestors is null
            ? []
            : CommissionCalculator.Calculate(profit, Schema, ancestors)
                .Select(share => new StoredCommission(
                    Guid.NewGuid(),
                    share.BeneficiaryExternalId,
                    share.Level,
                    share.Amount,
                    share.SchemaType,
                    false,
                    null))
                .ToArray();

        if (commissions.Length > 0)
            OutboxCount++;

        var stored = new StoredEvent(
            Guid.NewGuid(),
            externalId,
            userExternalId,
            profit,
            ancestors is null ? ProfitEventStatus.Pending : ProfitEventStatus.Calculated,
            DateTimeOffset.UtcNow,
            commissions);

        _events.Add(externalId, stored);
        return new PersistOutcome.Stored(stored, true);
    }
}

internal sealed class FakeUsersGateway : IUsersGateway
{
    public int Calls { get; private set; }

    public AncestorLookup Next { get; set; } = new AncestorLookup.Found(["l1", "l2", "l3"]);

    public Task<AncestorLookup> GetAncestorsAsync(string userExternalId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Next);
    }
}

internal sealed class NoopAccrualMetrics : IAccrualMetrics
{
    public void EventAccepted(string status)
    {
    }

    public void CommissionsCalculated(int count)
    {
    }

    public void HttpClientFailed(string client)
    {
    }

    public void OutboxFailed()
    {
    }

    public void SetOutboxDepth(int depth)
    {
    }
}
