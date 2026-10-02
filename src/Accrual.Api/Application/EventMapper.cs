using Accrual.Api.Domain;
using Contracts;

namespace Accrual.Api.Application;

internal static class EventMapper
{
    public static EventSummaryResponse ToSummary(StoredEvent ev) =>
        new(ev.ExternalId, ev.UserExternalId, ev.Profit, ev.Status.ToString(), ev.CreatedAt);

    public static EventDetailsResponse ToDetails(StoredEvent ev) =>
        new(
            ev.ExternalId,
            ev.UserExternalId,
            ev.Profit,
            ev.Status.ToString(),
            ev.CreatedAt,
            ev.Commissions
                .OrderBy(commission => commission.Level)
                .Select(commission => new CommissionResponse(
                    commission.Id,
                    commission.BeneficiaryExternalId,
                    commission.Level,
                    commission.Amount,
                    commission.SchemaType,
                    commission.IsPaid))
                .ToList());
}
