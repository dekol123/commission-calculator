using Contracts;

namespace Wallet.Domain;

public sealed class WalletAccount
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public static WalletAccount Open(string externalId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = externalId,
        CreatedAt = createdAt
    };
}

public enum InboxStatus
{
    Received,
    Claiming,
    Completed,
    Poison,
    Failed
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Payload { get; set; } = "";
    public InboxStatus Status { get; set; }
    public Guid? PayoutId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
}

public sealed class PayoutBatch
{
    public Guid PayoutId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public int ClaimFailures { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }

    public static PayoutBatch Lease(Guid payoutId, Guid leaseToken, DateTimeOffset now) => new()
    {
        PayoutId = payoutId,
        LeaseToken = leaseToken,
        LockedAt = now,
        NextAttemptAt = now
    };
}

public enum PayoutStatus
{
    Pending,
    Completed
}

public sealed class PayoutRecord
{
    public Guid Id { get; set; }
    public PayoutStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PayoutRecord Completed(Guid payoutId, DateTimeOffset createdAt) => new()
    {
        Id = payoutId,
        Status = PayoutStatus.Completed,
        CreatedAt = createdAt
    };
}

public sealed class PayoutLine
{
    public Guid Id { get; set; }
    public Guid PayoutId { get; set; }
    public Guid CommissionId { get; set; }
    public string BeneficiaryExternalId { get; set; } = "";
    public decimal Amount { get; set; }
    public string EventExternalId { get; set; } = "";
    public int Level { get; set; }
    public SchemaType SchemaType { get; set; }
    public DateTimeOffset PaidAt { get; set; }

    public static PayoutLine Paid(Guid payoutId, ClaimedCommissionResponse commission, DateTimeOffset paidAt) => new()
    {
        Id = Guid.NewGuid(),
        PayoutId = payoutId,
        CommissionId = commission.CommissionId,
        BeneficiaryExternalId = commission.BeneficiaryExternalId,
        Amount = commission.Amount,
        EventExternalId = commission.EventExternalId,
        Level = commission.Level,
        SchemaType = commission.SchemaType,
        PaidAt = paidAt
    };
}
