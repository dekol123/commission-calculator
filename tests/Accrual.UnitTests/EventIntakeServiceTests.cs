using Accrual.Application;
using Accrual.Domain;
using Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accrual.UnitTests;

public class EventIntakeServiceTests
{
    private readonly InMemoryEventIntakeStore _store = new();
    private readonly FakeUsersGateway _users = new();
    private readonly EventIntakeService _service;

    public EventIntakeServiceTests()
    {
        _service = new EventIntakeService(_store, _users, new NoopAccrualMetrics(), NullLogger<EventIntakeService>.Instance);
    }

    [Fact]
    public async Task First_event_is_calculated_once()
    {
        var created = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        var createdEvent = Assert.IsType<IntakeResult.Created>(created).Event;
        Assert.Equal([10m, 20m, 30m], createdEvent.Commissions.Select(c => c.Amount).ToArray());
        Assert.All(createdEvent.Commissions, c => Assert.Equal(SchemaType.Linear, c.SchemaType));
        Assert.Equal(1, _store.OutboxCount);

        var replay = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        Assert.IsType<IntakeResult.Existing>(replay);
        Assert.Equal(1, _users.Calls);
        Assert.Equal(1, _store.OutboxCount);
        Assert.Equal(3, createdEvent.Commissions.Count);
    }

    [Fact]
    public async Task Fibonacci_schema_is_snapshotted_on_the_commission()
    {
        _store.Schema = SchemaType.Fibonacci;

        var created = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        var commissions = Assert.IsType<IntakeResult.Created>(created).Event.Commissions;
        Assert.Equal([10m, 10m, 20m], commissions.Select(c => c.Amount).ToArray());
        Assert.All(commissions, c => Assert.Equal(SchemaType.Fibonacci, c.SchemaType));
    }

    [Fact]
    public async Task Different_payload_for_the_same_event_conflicts_and_does_not_recalculate()
    {
        await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        var conflict = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 50m), CancellationToken.None);

        Assert.IsType<IntakeResult.Conflict>(conflict);
        Assert.Equal(1, _users.Calls);
        Assert.Equal(1, _store.OutboxCount);
    }

    [Fact]
    public async Task Different_user_for_the_same_event_conflicts()
    {
        await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        var conflict = await _service.AcceptAsync(new CreateEventRequest("e1", "other", 1000m), CancellationToken.None);

        Assert.IsType<IntakeResult.Conflict>(conflict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task Non_positive_profit_is_stored_without_commissions(int profit)
    {
        var created = await _service.AcceptAsync(new CreateEventRequest("e1", "user", profit), CancellationToken.None);

        var stored = Assert.IsType<IntakeResult.Created>(created).Event;
        Assert.Empty(stored.Commissions);
        Assert.Equal(ProfitEventStatus.Calculated, stored.Status);
        Assert.Equal(0, _store.OutboxCount);
    }

    [Fact]
    public async Task Unknown_user_is_not_stored()
    {
        _users.Next = new AncestorLookup.NotFound();

        var result = await _service.AcceptAsync(new CreateEventRequest("e1", "missing", 1000m), CancellationToken.None);

        Assert.IsType<IntakeResult.UserNotFound>(result);
        Assert.Null(await _store.FindAsync("e1", CancellationToken.None));
    }

    [Fact]
    public async Task Open_circuit_does_not_store_the_event()
    {
        _users.Next = new AncestorLookup.CircuitOpen();

        var result = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        Assert.IsType<IntakeResult.UsersUnavailable>(result);
        Assert.Null(await _store.FindAsync("e1", CancellationToken.None));
    }

    [Fact]
    public async Task Users_outage_stores_a_pending_event_without_commissions()
    {
        _users.Next = new AncestorLookup.Unavailable();

        var result = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);

        var pending = Assert.IsType<IntakeResult.AcceptedPending>(result).Event;
        Assert.Equal(ProfitEventStatus.Pending, pending.Status);
        Assert.Empty(pending.Commissions);
        Assert.Equal(0, _store.OutboxCount);

        var replay = await _service.AcceptAsync(new CreateEventRequest("e1", "user", 1000m), CancellationToken.None);
        Assert.IsType<IntakeResult.AcceptedPending>(replay);
        Assert.Equal(1, _users.Calls);
    }
}
