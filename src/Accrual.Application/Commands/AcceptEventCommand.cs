using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed record AcceptEventCommand(CreateEventRequest Request) : IRequest<IntakeResult>;
