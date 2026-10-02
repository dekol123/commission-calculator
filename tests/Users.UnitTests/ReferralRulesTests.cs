using Users.Domain;

namespace Users.UnitTests;

public class ReferralRulesTests
{
    [Fact]
    public void Rejects_self_reference()
    {
        var violation = ReferralRules.ValidateAssignment("u", "u", [], 0);

        Assert.Equal(ReferralViolation.SelfReference, violation);
    }

    [Fact]
    public void Rejects_cycle_when_user_is_already_above_referrer()
    {
        var violation = ReferralRules.ValidateAssignment("u", "child", ["u", "root"], 0);

        Assert.Equal(ReferralViolation.Cycle, violation);
    }

    [Fact]
    public void Allows_exactly_ten_ancestors_for_a_leaf()
    {
        var ancestors = Enumerable.Range(1, 9).Select(i => $"a{i}").ToArray();

        var violation = ReferralRules.ValidateAssignment("user", "referrer", ancestors, descendantDepth: 0);

        Assert.Null(violation);
    }

    [Fact]
    public void Rejects_an_eleventh_ancestor()
    {
        var ancestors = Enumerable.Range(1, 10).Select(i => $"a{i}").ToArray();

        var violation = ReferralRules.ValidateAssignment("user", "referrer", ancestors, descendantDepth: 0);

        Assert.Equal(ReferralViolation.TooDeep, violation);
    }

    [Fact]
    public void Rejects_reparent_that_pushes_a_descendant_past_ten()
    {
        var ancestors = Enumerable.Range(1, 9).Select(i => $"a{i}").ToArray();

        var violation = ReferralRules.ValidateAssignment("user", "referrer", ancestors, descendantDepth: 1);

        Assert.Equal(ReferralViolation.TooDeep, violation);
    }

    [Fact]
    public void Allows_reparent_when_deepest_descendant_stays_at_ten()
    {
        var ancestors = Enumerable.Range(1, 8).Select(i => $"a{i}").ToArray();

        var violation = ReferralRules.ValidateAssignment("user", "referrer", ancestors, descendantDepth: 1);

        Assert.Null(violation);
    }

    [Fact]
    public void External_ids_are_case_sensitive()
    {
        var violation = ReferralRules.ValidateAssignment("User", "user", [], 0);

        Assert.Null(violation);
    }
}
