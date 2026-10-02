using Accrual.Domain;
using Contracts;

namespace Accrual.UnitTests;

public class CommissionCalculatorTests
{
    [Fact]
    public void Linear_pays_level_percent_of_profit()
    {
        var shares = CommissionCalculator.Calculate(1000m, SchemaType.Linear, ["l1", "l2", "l3"]);

        Assert.Equal([10m, 20m, 30m], shares.Select(share => share.Amount).ToArray());
        Assert.Equal([1, 2, 3], shares.Select(share => share.Level).ToArray());
        Assert.Equal(["l1", "l2", "l3"], shares.Select(share => share.BeneficiaryExternalId).ToArray());
        Assert.All(shares, share => Assert.Equal(SchemaType.Linear, share.SchemaType));
    }

    [Fact]
    public void Fibonacci_uses_standard_sequence_where_first_two_levels_are_one()
    {
        var shares = CommissionCalculator.Calculate(1000m, SchemaType.Fibonacci, ["l1", "l2", "l3"]);

        Assert.Equal([10m, 10m, 20m], shares.Select(share => share.Amount).ToArray());
        Assert.All(shares, share => Assert.Equal(SchemaType.Fibonacci, share.SchemaType));
    }

    [Fact]
    public void Fibonacci_weights_through_level_ten_sum_to_143_percent()
    {
        int[] expected = [1, 1, 2, 3, 5, 8, 13, 21, 34, 55];
        var ancestors = Enumerable.Range(1, 10).Select(level => $"u{level}").ToArray();

        Assert.Equal(expected, Enumerable.Range(1, 10).Select(CommissionCalculator.Fibonacci).ToArray());

        var shares = CommissionCalculator.Calculate(100m, SchemaType.Fibonacci, ancestors);

        Assert.Equal(expected.Select(weight => (decimal)weight).ToArray(), shares.Select(share => share.Amount).ToArray());
        Assert.Equal(143m, shares.Sum(share => share.Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Non_positive_profit_produces_no_commissions(int profit)
    {
        var shares = CommissionCalculator.Calculate(profit, SchemaType.Linear, ["l1", "l2"]);

        Assert.Empty(shares);
    }

    [Fact]
    public void Chain_is_cut_at_level_ten()
    {
        var ancestors = Enumerable.Range(1, 12).Select(level => $"u{level}").ToArray();

        var shares = CommissionCalculator.Calculate(1000m, SchemaType.Linear, ancestors);

        Assert.Equal(10, shares.Count);
        Assert.Equal(100m, shares[^1].Amount);
        Assert.Equal("u10", shares[^1].BeneficiaryExternalId);
        Assert.DoesNotContain(shares, share => share.BeneficiaryExternalId == "u11");
    }

    [Fact]
    public void Amount_that_rounds_to_zero_is_omitted()
    {
        var shares = CommissionCalculator.Calculate(0.4m, SchemaType.Linear, ["l1", "l2"]);

        Assert.Equal([new CommissionShare(2, "l2", 0.01m, SchemaType.Linear)], shares);
    }

    [Fact]
    public void Rounding_uses_midpoint_away_from_zero()
    {
        var shares = CommissionCalculator.Calculate(100.5m, SchemaType.Linear, ["l1"]);

        Assert.Equal([new CommissionShare(1, "l1", 1.01m, SchemaType.Linear)], shares);
    }

    [Fact]
    public void Empty_ancestor_chain_produces_no_commissions()
    {
        var shares = CommissionCalculator.Calculate(1000m, SchemaType.Fibonacci, []);

        Assert.Empty(shares);
    }
}
