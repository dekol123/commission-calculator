using Accrual.Domain;
using Contracts;
using Microsoft.Extensions.Logging;

namespace Accrual.Application;

public sealed class EventIntakeService(
    IEventIntakeStore store,
    IUsersGateway users,
    IAccrualMetrics metrics,
    ILogger<EventIntakeService> logger)
{
    public async Task<IntakeResult> AcceptAsync(CreateEventRequest request, CancellationToken cancellationToken)
    {
        if (request is null
            || !ExternalIds.IsValid(request.ExternalId)
            || !ExternalIds.IsValid(request.UserExternalId))
        {
            return new IntakeResult.Invalid();
        }

        var existing = await store.FindAsync(request.ExternalId, cancellationToken);
        if (existing is not null)
            return FromExisting(existing, request);

        var lookup = await users.GetAncestorsAsync(request.UserExternalId, cancellationToken);
        switch (lookup)
        {
            case AncestorLookup.NotFound:
                logger.LogInformation(
                    "Rejected event {EventExternalId} because user {UserExternalId} does not exist",
                    request.ExternalId,
                    request.UserExternalId);
                return new IntakeResult.UserNotFound();

            case AncestorLookup.CircuitOpen:
                logger.LogWarning(
                    "Users circuit is open. Event {EventExternalId} was not stored",
                    request.ExternalId);
                return new IntakeResult.UsersUnavailable();

            case AncestorLookup.Unavailable:
                logger.LogWarning(
                    "Users is unavailable. Event {EventExternalId} is stored pending calculation",
                    request.ExternalId);
                var pending = await store.SavePendingAsync(
                    request.ExternalId,
                    request.UserExternalId,
                    request.Profit,
                    cancellationToken);
                return FromPersist(pending, request, pendingMeansAccepted: true);

            case AncestorLookup.Found found:
                var saved = await store.SaveCalculatedAsync(
                    request.ExternalId,
                    request.UserExternalId,
                    request.Profit,
                    found.AncestorsNearestFirst,
                    cancellationToken);
                return FromPersist(saved, request, pendingMeansAccepted: false);

            default:
                throw new InvalidOperationException($"Unexpected ancestor lookup {lookup.GetType().Name}.");
        }
    }

    private IntakeResult FromPersist(PersistOutcome outcome, CreateEventRequest request, bool pendingMeansAccepted)
    {
        switch (outcome)
        {
            case PersistOutcome.Conflict conflict:
                return new IntakeResult.Conflict(conflict.Existing);
            case PersistOutcome.Stored stored when !stored.Created:
                return FromExisting(stored.Event, request);
            case PersistOutcome.Stored stored:
                metrics.EventAccepted(stored.Event.Status.ToString());
                if (stored.Event.Commissions.Count > 0)
                    metrics.CommissionsCalculated(stored.Event.Commissions.Count);

                logger.LogInformation(
                    "Accepted event {EventExternalId} for {UserExternalId} with status {Status} and {CommissionCount} commissions",
                    stored.Event.ExternalId,
                    stored.Event.UserExternalId,
                    stored.Event.Status,
                    stored.Event.Commissions.Count);

                if (pendingMeansAccepted || stored.Event.Status is ProfitEventStatus.Pending or ProfitEventStatus.Calculating)
                    return new IntakeResult.AcceptedPending(stored.Event);

                return new IntakeResult.Created(stored.Event);
            default:
                throw new InvalidOperationException($"Unexpected persist outcome {outcome.GetType().Name}.");
        }
    }

    private static IntakeResult FromExisting(StoredEvent existing, CreateEventRequest request)
    {
        if (existing.UserExternalId != request.UserExternalId || existing.Profit != request.Profit)
            return new IntakeResult.Conflict(existing);

        if (existing.Status is ProfitEventStatus.Pending or ProfitEventStatus.Calculating)
            return new IntakeResult.AcceptedPending(existing);

        return new IntakeResult.Existing(existing);
    }
}
