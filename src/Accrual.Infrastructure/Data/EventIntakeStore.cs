using System.Text.Json;
using Accrual.Application;
using Accrual.Domain;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Accrual.Infrastructure;

public sealed class EventIntakeStore(AccrualDb db, TimeProvider time) : IEventIntakeStore
{
    public async Task<StoredEvent?> FindAsync(string externalId, CancellationToken cancellationToken)
    {
        var ev = await db.Events.AsNoTracking()
            .Include(item => item.Commissions)
            .SingleOrDefaultAsync(item => item.ExternalId == externalId, cancellationToken);
        return ev is null ? null : EventMaps.ToStored(ev);
    }

    public Task<PersistOutcome> SavePendingAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        CancellationToken cancellationToken) =>
        SaveAsync(externalId, userExternalId, profit, ancestors: null, cancellationToken);

    public Task<PersistOutcome> SaveCalculatedAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        IReadOnlyList<string> ancestorsNearestFirst,
        CancellationToken cancellationToken) =>
        SaveAsync(externalId, userExternalId, profit, ancestorsNearestFirst, cancellationToken);

    private async Task<PersistOutcome> SaveAsync(
        string externalId,
        string userExternalId,
        decimal profit,
        IReadOnlyList<string>? ancestors,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<PersistOutcome>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var existing = await db.Events
                .Include(item => item.Commissions)
                .SingleOrDefaultAsync(item => item.ExternalId == externalId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                if (existing.Profit != profit || existing.UserExternalId != userExternalId)
                    return new PersistOutcome.Conflict(EventMaps.ToStored(existing));

                return new PersistOutcome.Stored(EventMaps.ToStored(existing), false);
            }

            var now = time.GetUtcNow();
            var profitEvent = new ProfitEvent
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                UserExternalId = userExternalId,
                Profit = profit,
                Status = ancestors is null ? ProfitEventStatus.Pending : ProfitEventStatus.Calculated,
                CreatedAt = now,
                NextCalculationAt = now
            };

            if (ancestors is not null)
            {
                var schema = await db.SchemaSettings.SingleAsync(
                    setting => setting.Id == SchemaSetting.SingletonId,
                    cancellationToken);
                var shares = CommissionCalculator.Calculate(profit, schema.SchemaType, ancestors);
                foreach (var share in shares)
                {
                    profitEvent.Commissions.Add(new CommissionLine
                    {
                        Id = Guid.NewGuid(),
                        EventId = profitEvent.Id,
                        Level = share.Level,
                        BeneficiaryExternalId = share.BeneficiaryExternalId,
                        Amount = share.Amount,
                        SchemaType = share.SchemaType
                    });
                }

                AddOutbox(profitEvent, now);
            }

            db.Events.Add(profitEvent);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new PersistOutcome.Stored(EventMaps.ToStored(profitEvent), true);
            }
            catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var raced = await db.Events.Include(item => item.Commissions)
                    .SingleAsync(item => item.ExternalId == externalId, cancellationToken);
                if (raced.Profit != profit || raced.UserExternalId != userExternalId)
                    return new PersistOutcome.Conflict(EventMaps.ToStored(raced));

                return new PersistOutcome.Stored(EventMaps.ToStored(raced), false);
            }
        });
    }

    private void AddOutbox(ProfitEvent profitEvent, DateTimeOffset now)
    {
        if (profitEvent.Commissions.Count == 0)
            return;

        var messageId = Guid.NewGuid();
        var payload = new WalletInboxRequest(
            messageId,
            profitEvent.Commissions.Select(line => new WalletInboxCommission(
                line.Id,
                line.BeneficiaryExternalId,
                line.Amount,
                profitEvent.ExternalId,
                line.Level,
                line.SchemaType)).ToList());

        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Payload = JsonSerializer.Serialize(payload, ApiJson.Options),
            Status = OutboxStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        });
    }
}

public sealed class EventQueryStore(AccrualDb db) : IEventQueryStore
{
    public async Task<IReadOnlyList<StoredEvent>> ListByUserAsync(string userExternalId, CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking()
            .Where(item => item.UserExternalId == userExternalId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return events.Select(item => EventMaps.ToStored(item)).ToList();
    }

    public async Task<StoredEvent?> FindAsync(string externalId, CancellationToken cancellationToken)
    {
        var ev = await db.Events.AsNoTracking()
            .Include(item => item.Commissions)
            .SingleOrDefaultAsync(item => item.ExternalId == externalId, cancellationToken);
        return ev is null ? null : EventMaps.ToStored(ev);
    }
}

public sealed class SchemaService(AccrualDb db, ILogger<SchemaService> logger) : ISchemaSettings
{
    public async Task<SchemaType> GetAsync(CancellationToken cancellationToken) =>
        await db.SchemaSettings
            .Where(setting => setting.Id == SchemaSetting.SingletonId)
            .Select(setting => setting.SchemaType)
            .SingleAsync(cancellationToken);

    public async Task<SchemaType> SetAsync(SchemaType schema, CancellationToken cancellationToken)
    {
        var setting = await db.SchemaSettings.SingleAsync(item => item.Id == SchemaSetting.SingletonId, cancellationToken);
        setting.SchemaType = schema;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Commission schema switched to {SchemaType}", schema);
        return schema;
    }
}
