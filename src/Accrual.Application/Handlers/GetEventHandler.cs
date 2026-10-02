using Accrual.Domain;
using MediatR;

namespace Accrual.Application;

public sealed class GetEventHandler(IEventQueryStore events) : IRequestHandler<GetEventQuery, StoredEvent?>
{
    public Task<StoredEvent?> Handle(GetEventQuery query, CancellationToken cancellationToken) =>
        events.FindAsync(query.ExternalId, cancellationToken);
}
