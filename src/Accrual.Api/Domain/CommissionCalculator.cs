using Contracts;

namespace Accrual.Api.Domain;

public sealed record CommissionShare(
    int Level,
    string BeneficiaryExternalId,
    decimal Amount,
    SchemaType SchemaType);

public static class CommissionCalculator
{
    public const int MaxDepth = 10;

    public static IReadOnlyList<CommissionShare> Calculate(
        decimal profit,
        SchemaType schema,
        IReadOnlyList<string> ancestorsNearestFirst)
    {
        ArgumentNullException.ThrowIfNull(ancestorsNearestFirst);

        if (profit <= 0 || ancestorsNearestFirst.Count == 0)
            return [];

        var depth = Math.Min(ancestorsNearestFirst.Count, MaxDepth);
        var shares = new List<CommissionShare>(depth);
        for (var index = 0; index < depth; index++)
        {
            var level = index + 1;
            var weight = schema switch
            {
                SchemaType.Linear => level,
                SchemaType.Fibonacci => Fibonacci(level),
                _ => throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unknown commission schema.")
            };

            var amount = decimal.Round(weight * profit / 100m, 2, MidpointRounding.AwayFromZero);
            if (amount == 0)
                continue;

            shares.Add(new CommissionShare(level, ancestorsNearestFirst[index], amount, schema));
        }

        return shares;
    }

    public static int Fibonacci(int level)
    {
        if (level < 1)
            throw new ArgumentOutOfRangeException(nameof(level));

        if (level <= 2)
            return 1;

        var previous = 1;
        var current = 1;
        for (var step = 3; step <= level; step++)
        {
            var next = previous + current;
            previous = current;
            current = next;
        }

        return current;
    }
}
