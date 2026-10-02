namespace Contracts;

public sealed record WalletBalanceResponse(string ExternalId, decimal Balance);

public sealed record PayoutHistoryItemResponse(
    Guid PayoutId,
    Guid CommissionId,
    string EventExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    DateTimeOffset PaidAt);

public sealed record WalletInboxCommission(
    Guid CommissionId,
    string BeneficiaryExternalId,
    decimal Amount,
    string EventExternalId,
    int Level,
    SchemaType SchemaType);

public sealed record WalletInboxRequest(
    Guid MessageId,
    IReadOnlyList<WalletInboxCommission> Commissions);
