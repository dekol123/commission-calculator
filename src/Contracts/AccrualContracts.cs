namespace Contracts;

public sealed record CreateEventRequest(string ExternalId, string UserExternalId, decimal Profit);

public sealed record EventSummaryResponse(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CommissionResponse(
    Guid CommissionId,
    string BeneficiaryExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    bool Paid);

public sealed record EventDetailsResponse(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CommissionResponse> Commissions);

public sealed record SchemaResponse(SchemaType SchemaType);

public sealed record SetSchemaRequest(SchemaType SchemaType);

public sealed record ClaimPayoutRequest(Guid PayoutId, IReadOnlyList<Guid> CommissionIds);

public sealed record ClaimedCommissionResponse(
    Guid CommissionId,
    string BeneficiaryExternalId,
    decimal Amount,
    string EventExternalId,
    int Level,
    SchemaType SchemaType);

public sealed record ClaimPayoutResponse(
    Guid PayoutId,
    IReadOnlyList<ClaimedCommissionResponse> Commissions);
