using Accrual.Domain;
using MediatR;

namespace Accrual.Application;

public sealed record GetEventQuery(string ExternalId) : IRequest<StoredEvent?>;
