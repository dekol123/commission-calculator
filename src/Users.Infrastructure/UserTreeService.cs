using System.Data;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Users.Application;
using Users.Domain;

namespace Users.Infrastructure;

public sealed class UserTreeService(UsersDb db, ILogger<UserTreeService> logger) : IUserTree
{
    public async Task<(UserResponse User, bool Created)> CreateAsync(string externalId, CancellationToken cancellationToken)
    {
        var existing = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.ExternalId == externalId, cancellationToken);
        if (existing is not null)
            return (new UserResponse(existing.ExternalId), false);

        db.Users.Add(new UserAccount { Id = Guid.NewGuid(), ExternalId = externalId });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created user {UserExternalId}", externalId);
            return (new UserResponse(externalId), true);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            return (new UserResponse(externalId), false);
        }
    }

    public Task<ReferralChange> AssignReferrerAsync(
        string externalId,
        string referrerExternalId,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync<ReferralChange>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var user = await db.Users.SingleOrDefaultAsync(account => account.ExternalId == externalId, cancellationToken);
            if (user is null)
                return (ReferralChange)new ReferralChange.MissingUser();

            var referrer = await db.Users.SingleOrDefaultAsync(
                account => account.ExternalId == referrerExternalId,
                cancellationToken);
            if (referrer is null)
                return new ReferralChange.MissingReferrer();

            if (user.ReferrerId == referrer.Id)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ReferralChange.Updated();
            }

            var ancestors = await LoadAncestorsAsync(referrer.Id, cancellationToken);
            var descendantDepth = await MaxDescendantDepthAsync(user.Id, cancellationToken);
            var violation = ReferralRules.ValidateAssignment(
                user.ExternalId,
                referrer.ExternalId,
                ancestors,
                descendantDepth);
            if (violation is not null)
            {
                logger.LogInformation(
                    "Rejected referrer {ReferrerExternalId} for {UserExternalId}: {Violation}",
                    referrerExternalId,
                    externalId,
                    violation);
                return new ReferralChange.Rejected(violation.Value);
            }

            user.ReferrerId = referrer.Id;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Set referrer {ReferrerExternalId} for {UserExternalId}",
                referrerExternalId,
                externalId);
            return new ReferralChange.Updated();
        });
    }

    public async Task<bool> ClearReferrerAsync(string externalId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(account => account.ExternalId == externalId, cancellationToken);
        if (user is null)
            return false;

        user.ReferrerId = null;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Cleared referrer for {UserExternalId}", externalId);
        return true;
    }

    public async Task<IReadOnlyList<TreeNodeResponse>?> AncestorsAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(user => user.ExternalId == externalId, cancellationToken))
            return null;

        return await db.Database.SqlQuery<ExternalLevelRow>($"""
            WITH RECURSIVE chain AS (
                SELECT referrer_id AS id, 1 AS level
                FROM users
                WHERE external_id = {externalId} AND referrer_id IS NOT NULL
                UNION ALL
                SELECT parent.referrer_id, chain.level + 1
                FROM users AS parent
                INNER JOIN chain ON parent.id = chain.id
                WHERE parent.referrer_id IS NOT NULL AND chain.level < 32
            )
            SELECT ancestor.external_id AS external_id, chain.level AS level
            FROM chain
            INNER JOIN users AS ancestor ON ancestor.id = chain.id
            ORDER BY chain.level
            """).Select(row => new TreeNodeResponse(row.ExternalId, row.Level)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TreeNodeResponse>?> DescendantsAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(user => user.ExternalId == externalId, cancellationToken))
            return null;

        var maxDepth = ReferralRules.MaxAncestors;
        return await db.Database.SqlQuery<ExternalLevelRow>($"""
            WITH RECURSIVE down AS (
                SELECT child.id, child.external_id, 1 AS level
                FROM users AS child
                INNER JOIN users AS parent ON parent.id = child.referrer_id
                WHERE parent.external_id = {externalId}
                UNION ALL
                SELECT grandchild.id, grandchild.external_id, down.level + 1
                FROM users AS grandchild
                INNER JOIN down ON grandchild.referrer_id = down.id
                WHERE down.level < {maxDepth}
            )
            SELECT down.external_id AS external_id, down.level AS level
            FROM down
            ORDER BY down.level, down.external_id
            """).Select(row => new TreeNodeResponse(row.ExternalId, row.Level)).ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> LoadAncestorsAsync(Guid referrerId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<ExternalLevelRow>($"""
            WITH RECURSIVE chain AS (
                SELECT referrer_id AS id, 1 AS level
                FROM users
                WHERE id = {referrerId} AND referrer_id IS NOT NULL
                UNION ALL
                SELECT parent.referrer_id, chain.level + 1
                FROM users AS parent
                INNER JOIN chain ON parent.id = chain.id
                WHERE parent.referrer_id IS NOT NULL AND chain.level < 32
            )
            SELECT ancestor.external_id AS external_id, chain.level AS level
            FROM chain
            INNER JOIN users AS ancestor ON ancestor.id = chain.id
            ORDER BY chain.level
            """).ToListAsync(cancellationToken);

        return rows.Select(row => row.ExternalId).ToList();
    }

    private async Task<int> MaxDescendantDepthAsync(Guid userId, CancellationToken cancellationToken)
    {
        var depth = await db.Database.SqlQuery<DepthResult>($"""
            WITH RECURSIVE down AS (
                SELECT id, 0 AS depth
                FROM users
                WHERE id = {userId}
                UNION ALL
                SELECT child.id, down.depth + 1
                FROM users AS child
                INNER JOIN down ON child.referrer_id = down.id
                WHERE down.depth < 32
            )
            SELECT COALESCE(MAX(down.depth), 0)::int AS value
            FROM down
            """).SingleAsync(cancellationToken);

        return depth.value;
    }
}

internal static class PostgresErrors
{
    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }
}
