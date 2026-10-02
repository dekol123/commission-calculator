using Contracts;
using Microsoft.AspNetCore.Mvc;
using Users.Api.Domain;
using Users.Api.Services;

namespace Users.Api.Controllers;

[ApiController]
[Route("users")]
public sealed class UsersController(UserTreeService users) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ExternalIds.IsValid(request.ExternalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var (user, created) = await users.CreateAsync(request.ExternalId, cancellationToken);
        if (!created)
            return Ok(user);

        return Created($"/users/{Uri.EscapeDataString(user.ExternalId)}/up", user);
    }

    [HttpPut("{externalId}/referrer")]
    public async Task<IActionResult> AssignReferrer(
        string externalId,
        SetReferrerRequest request,
        CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(externalId) || request is null || !ExternalIds.IsValid(request.ReferrerExternalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var result = await users.AssignReferrerAsync(externalId, request.ReferrerExternalId, cancellationToken);
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

        var cleared = await users.ClearReferrerAsync(externalId, cancellationToken);
        return cleared ? NoContent() : NotFound(new ErrorResponse("User was not found."));
    }

    [HttpGet("{externalId}/up")]
    public async Task<ActionResult<IReadOnlyList<TreeNodeResponse>>> Up(string externalId, CancellationToken cancellationToken)
    {
        var ancestors = await users.AncestorsAsync(externalId, cancellationToken);
        return ancestors is null ? NotFound(new ErrorResponse("User was not found.")) : Ok(ancestors);
    }

    [HttpGet("{externalId}/down")]
    public async Task<ActionResult<IReadOnlyList<TreeNodeResponse>>> Down(string externalId, CancellationToken cancellationToken)
    {
        var descendants = await users.DescendantsAsync(externalId, cancellationToken);
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
