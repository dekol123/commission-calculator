using Contracts;

namespace Accrual.Api.Domain;

public enum ProfitEventStatus
{
    Pending,
    Calculating,
    Calculated,
    Rejected
}

public sealed record StoredCommission(
    Guid Id,
    string BeneficiaryExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    bool IsPaid,
    Guid? PayoutId);

public sealed record StoredEvent(
    Guid Id,
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    ProfitEventStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StoredCommission> Commissions);

public abstract record PersistOutcome
{
    public sealed record Stored(StoredEvent Event, bool Created) : PersistOutcome;

    public sealed record Conflict(StoredEvent Existing) : PersistOutcome;
}

public sealed record ClaimedCommission(
    Guid CommissionId,
    string BeneficiaryExternalId,
    decimal Amount,
    string EventExternalId,
    int Level,
    SchemaType SchemaType);
