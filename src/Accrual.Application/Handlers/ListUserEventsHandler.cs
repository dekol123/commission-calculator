using Accrual.Domain;
using MediatR;

namespace Accrual.Application;

public sealed class ListUserEventsHandler(IEventQueryStore events) : IRequestHandler<ListUserEventsQuery, IReadOnlyList<StoredEvent>>
{
    public Task<IReadOnlyList<StoredEvent>> Handle(ListUserEventsQuery query, CancellationToken cancellationToken) =>
        events.ListByUserAsync(query.UserExternalId, cancellationToken);
}
