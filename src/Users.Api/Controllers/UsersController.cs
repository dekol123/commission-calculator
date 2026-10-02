using Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Users.Application;
using Users.Domain;

namespace Users.Api.Controllers;

[ApiController]
[Route("users")]
public sealed class UsersController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ExternalIds.IsValid(request.ExternalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var result = await mediator.Send(new CreateUserCommand(request.ExternalId), cancellationToken);
        if (!result.Created)
            return Ok(result.User);

        return Created($"/users/{Uri.EscapeDataString(result.User.ExternalId)}/up", result.User);
    }

    [HttpPut("{externalId}/referrer")]
    public async Task<IActionResult> AssignReferrer(
        string externalId,
        SetReferrerRequest request,
        CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(externalId) || request is null || !ExternalIds.IsValid(request.ReferrerExternalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var result = await mediator.Send(new AssignReferrerCommand(externalId, request.ReferrerExternalId), cancellationToken);
        return result switch
        {
            ReferralChange.Updated => NoContent(),
            ReferralChange.MissingUser => NotFound(new ErrorResponse("User was not found.")),
            ReferralChange.MissingReferrer => NotFound(new ErrorResponse("Referrer was not found.")),
            ReferralChange.Rejected rejected => BadRequest(new ErrorResponse(Describe(rejected.Violation))),
            _ => throw new InvalidOperationException($"Unexpected referral result {result.GetType().Name}.")
        };
    }

    [HttpDelete("{externalId}/referrer")]
    public async Task<IActionResult> ClearReferrer(string externalId, CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(externalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var cleared = await mediator.Send(new ClearReferrerCommand(externalId), cancellationToken);
        return cleared ? NoContent() : NotFound(new ErrorResponse("User was not found."));
    }

    [HttpGet("{externalId}/up")]
    public async Task<ActionResult<IReadOnlyList<TreeNodeResponse>>> Up(string externalId, CancellationToken cancellationToken)
    {
        var ancestors = await mediator.Send(new GetAncestorsQuery(externalId), cancellationToken);
        return ancestors is null ? NotFound(new ErrorResponse("User was not found.")) : Ok(ancestors);
    }

    [HttpGet("{externalId}/down")]
    public async Task<ActionResult<IReadOnlyList<TreeNodeResponse>>> Down(string externalId, CancellationToken cancellationToken)
    {
        var descendants = await mediator.Send(new GetDescendantsQuery(externalId), cancellationToken);
        return descendants is null ? NotFound(new ErrorResponse("User was not found.")) : Ok(descendants);
    }

    private static string Describe(ReferralViolation violation) => violation switch
    {
        ReferralViolation.SelfReference => "User cannot refer themselves.",
        ReferralViolation.Cycle => "Referral would create a cycle.",
        ReferralViolation.TooDeep => "Referral would make a chain longer than 10 ancestors.",
        _ => "Referral was rejected."
    };
}
