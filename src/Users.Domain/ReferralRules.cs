namespace Users.Domain;

public enum ReferralViolation
{
    SelfReference,
    Cycle,
    TooDeep
}

public static class ReferralRules
{
    public const int MaxAncestors = 10;

    public static ReferralViolation? ValidateAssignment(
        string userExternalId,
        string referrerExternalId,
        IReadOnlyList<string> referrerAncestorsNearestFirst,
        int descendantDepth)
    {
        ArgumentNullException.ThrowIfNull(referrerAncestorsNearestFirst);

        if (descendantDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(descendantDepth));

        if (string.Equals(userExternalId, referrerExternalId, StringComparison.Ordinal))
            return ReferralViolation.SelfReference;

        if (referrerAncestorsNearestFirst.Contains(userExternalId, StringComparer.Ordinal))
            return ReferralViolation.Cycle;

        var ancestorCount = referrerAncestorsNearestFirst.Count + 1;
        if (ancestorCount > MaxAncestors || ancestorCount + descendantDepth > MaxAncestors)
            return ReferralViolation.TooDeep;

        return null;
    }
}
