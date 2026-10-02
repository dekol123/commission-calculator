using Accrual.Application;
using Accrual.Domain;
using Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accrual.UnitTests;

internal sealed class MemoryCommission : IClaimableCommission
{
    public Guid Id { get; init; }
    public bool IsPaid { get; set; }
    public Guid? PayoutId { get; set; }
    public string BeneficiaryExternalId { get; init; } = "";
    public decimal Amount { get; init; }
    public string EventExternalId { get; init; } = "";
    public int Level { get; init; }
    public SchemaType SchemaType { get; init; }
}

internal sealed class InMemoryClaimStore : IClaimStore
{
    private readonly List<MemoryCommission> _rows = [];

    public MemoryCommission Seed(
        decimal amount = 10m,
        string beneficiary = "partner",
        string eventExternalId = "e1",
        int level = 1)
    {
        var row = new MemoryCommission
        {
            Id = Guid.NewGuid(),
            BeneficiaryExternalId = beneficiary,
            Amount = amount,
            EventExternalId = eventExternalId,
            Level = level,
            SchemaType = SchemaType.Linear
        };
        _rows.Add(row);
        return row;
    }

    public Task<IReadOnlyList<ClaimedCommission>> ClaimAsync(
        Guid payoutId,
        IReadOnlyList<Guid> commissionIds,
        CancellationToken cancellationToken)
    {
        var loaded = _rows.Where(row => row.PayoutId == payoutId || commissionIds.Contains(row.Id)).ToList();
        var claimed = ClaimTransition.Apply(loaded, payoutId, commissionIds);
        IReadOnlyList<ClaimedCommission> mapped = claimed.Select(row => new ClaimedCommission(
            row.Id,
            row.BeneficiaryExternalId,
            row.Amount,
            row.EventExternalId,
            row.Level,
            row.SchemaType)).ToList();
        return Task.FromResult(mapped);
    }
}

public class ClaimServiceTests
{
    private readonly InMemoryClaimStore _store = new();
    private readonly ClaimService _service;

    public ClaimServiceTests()
    {
        _service = new ClaimService(_store, NullLogger<ClaimService>.Instance);
    }

    [Fact]
    public async Task Same_payout_id_returns_the_original_set()
    {
        var first = _store.Seed(amount: 10m, beneficiary: "a");
        var second = _store.Seed(amount: 20m, beneficiary: "b");
        var payoutId = Guid.NewGuid();

        var initial = await _service.ClaimAsync(
            new ClaimPayoutRequest(payoutId, [first.Id, second.Id]),
            CancellationToken.None);
        var replay = await _service.ClaimAsync(
            new ClaimPayoutRequest(payoutId, [first.Id]),
            CancellationToken.None);

        Assert.Equal(
            initial.Commissions.Select(c => c.CommissionId).OrderBy(id => id),
            replay.Commissions.Select(c => c.CommissionId).OrderBy(id => id));
        Assert.Equal(2, replay.Commissions.Count);
        Assert.True(first.IsPaid);
        Assert.Equal(payoutId, second.PayoutId);
    }

    [Fact]
    public async Task Another_payout_cannot_take_claimed_commissions()
    {
        var commission = _store.Seed();
        var firstPayout = Guid.NewGuid();
        await _service.ClaimAsync(new ClaimPayoutRequest(firstPayout, [commission.Id]), CancellationToken.None);

        var second = await _service.ClaimAsync(
            new ClaimPayoutRequest(Guid.NewGuid(), [commission.Id]),
            CancellationToken.None);

        Assert.Empty(second.Commissions);
        Assert.Equal(firstPayout, commission.PayoutId);
    }

    [Fact]
    public async Task Unknown_commission_ids_are_ignored()
    {
        var payoutId = Guid.NewGuid();

        var result = await _service.ClaimAsync(
            new ClaimPayoutRequest(payoutId, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Empty(result.Commissions);
    }
}
