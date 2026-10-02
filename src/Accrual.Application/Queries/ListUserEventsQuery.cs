using Accrual.Domain;
using MediatR;

namespace Accrual.Application;

public sealed record ListUserEventsQuery(string UserExternalId) : IRequest<IReadOnlyList<StoredEvent>>;
