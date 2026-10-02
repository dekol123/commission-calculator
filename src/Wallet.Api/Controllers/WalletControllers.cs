using Contracts;
using Microsoft.AspNetCore.Mvc;
using Wallet.Api.Data;

namespace Wallet.Api.Controllers;

[ApiController]
public sealed class WalletsController(WalletQueryService wallets) : ControllerBase
{
    [HttpGet("/wallets/{externalId}")]
    public async Task<ActionResult<WalletBalanceResponse>> Balance(string externalId, CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(externalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        return Ok(await wallets.GetBalanceAsync(externalId, cancellationToken));
    }

    [HttpGet("/wallets/{externalId}/payouts")]
    public async Task<ActionResult<IReadOnlyList<PayoutHistoryItemResponse>>> History(
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(externalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        return Ok(await wallets.HistoryAsync(externalId, cancellationToken));
    }
}

[ApiController]
public sealed class InboxController(InboxService inbox) : ControllerBase
{
    [HttpPost("/internal/inbox")]
    public async Task<IActionResult> Accept(WalletInboxRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.MessageId == Guid.Empty || request.Commissions is null)
            return BadRequest(new ErrorResponse("Message id and commissions are required."));

        await inbox.AcceptAsync(request, cancellationToken);
        return NoContent();
    }
}
