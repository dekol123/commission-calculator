using Microsoft.EntityFrameworkCore;

namespace Accrual.Infrastructure;

public sealed record OutboxWork(Guid Id, Guid DispatchToken, Guid MessageId, string Payload, int Attempts);

public sealed class OutboxStore(AccrualDb db, TimeProvider time)
{
    public async Task ReleaseStaleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var staleBefore = now.Subtract(TimeSpan.FromSeconds(30));
        var pending = OutboxStatus.Pending.ToString();
        var dispatching = OutboxStatus.Dispatching.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages
            SET status = {pending},
                dispatch_token = NULL,
                next_attempt_at = {now}
            WHERE status = {dispatching}
              AND locked_at IS NOT NULL
              AND locked_at < {staleBefore}
            """,
            cancellationToken);
    }

    public async Task<OutboxWork?> TakeOneAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var token = Guid.NewGuid();
        var pending = OutboxStatus.Pending.ToString();
        var dispatching = OutboxStatus.Dispatching.ToString();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages AS message
            SET status = {dispatching},
                dispatch_token = {token},
                locked_at = {now},
                attempts = message.attempts + 1
            WHERE message.id = (
                SELECT candidate.id
                FROM outbox_messages AS candidate
                WHERE candidate.status = {pending}
                  AND candidate.next_attempt_at <= {now}
                ORDER BY candidate.created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            """,
            cancellationToken);

        if (updated == 0)
            return null;

        var message = await db.Outbox.AsNoTracking().SingleAsync(item => item.DispatchToken == token, cancellationToken);
        return new OutboxWork(message.Id, token, message.MessageId, message.Payload, message.Attempts);
    }

    public async Task MarkDeliveredAsync(OutboxWork work, CancellationToken cancellationToken)
    {
        var delivered = OutboxStatus.Delivered.ToString();
        var dispatching = OutboxStatus.Dispatching.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages
            SET status = {delivered},
                dispatch_token = NULL,
                last_error = NULL
            WHERE id = {work.Id}
              AND dispatch_token = {work.DispatchToken}
              AND status = {dispatching}
            """,
            cancellationToken);
    }

    public async Task MarkFailedAsync(OutboxWork work, string error, int maxAttempts, CancellationToken cancellationToken)
    {
        var message = await db.Outbox.SingleOrDefaultAsync(
            item => item.Id == work.Id && item.DispatchToken == work.DispatchToken && item.Status == OutboxStatus.Dispatching,
            cancellationToken);
        if (message is null)
            return;

        message.LastError = error.Length > 2000 ? error[..2000] : error;
        message.DispatchToken = null;
        if (message.Attempts >= maxAttempts)
        {
            message.Status = OutboxStatus.Failed;
        }
        else
        {
            var seconds = Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8)));
            message.Status = OutboxStatus.Pending;
            message.NextAttemptAt = time.GetUtcNow().AddSeconds(seconds);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> DepthAsync(CancellationToken cancellationToken) =>
        db.Outbox.CountAsync(
            message => message.Status == OutboxStatus.Pending || message.Status == OutboxStatus.Dispatching,
            cancellationToken);
}
