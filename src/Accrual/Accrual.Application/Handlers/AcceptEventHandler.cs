using MediatR;

namespace Accrual.Application;

public sealed class AcceptEventHandler(EventIntakeService intake) : IRequestHandler<AcceptEventCommand, IntakeResult>
{
    public Task<IntakeResult> Handle(AcceptEventCommand command, CancellationToken cancellationToken) =>
        intake.AcceptAsync(command.Request, cancellationToken);
}
