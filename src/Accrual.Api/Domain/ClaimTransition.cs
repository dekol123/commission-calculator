namespace Accrual.Api.Domain;

public interface IClaimableCommission
{
    Guid Id { get; }
    bool IsPaid { get; set; }
    Guid? PayoutId { get; set; }
}

public static class ClaimTransition
{
    public static List<T> Apply<T>(
        IReadOnlyList<T> loaded,
        Guid payoutId,
        IReadOnlyList<Guid> requestedIds)
        where T : IClaimableCommission
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(requestedIds);

        var alreadyBound = loaded.Where(row => row.PayoutId == payoutId).ToList();
        if (alreadyBound.Count > 0)
            return alreadyBound;

        var byId = new Dictionary<Guid, T>();
        foreach (var row in loaded)
            byId.TryAdd(row.Id, row);

        var claimed = new List<T>();
        foreach (var id in requestedIds.Distinct())
        {
            if (!byId.TryGetValue(id, out var row) || row.IsPaid)
                continue;

            row.IsPaid = true;
            row.PayoutId = payoutId;
            claimed.Add(row);
        }

        return claimed;
    }
}
