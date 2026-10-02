using Accrual.Domain;

namespace Accrual.Application;

public abstract record IntakeResult
{
    public sealed record Created(StoredEvent Event) : IntakeResult;

    public sealed record Existing(StoredEvent Event) : IntakeResult;

    public sealed record AcceptedPending(StoredEvent Event) : IntakeResult;

    public sealed record Conflict(StoredEvent Event) : IntakeResult;

    public sealed record UserNotFound : IntakeResult;

    public sealed record UsersUnavailable : IntakeResult;

    public sealed record Invalid : IntakeResult;
}
