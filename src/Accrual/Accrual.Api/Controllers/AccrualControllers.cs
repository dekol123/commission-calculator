using Accrual.Application;
using Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Accrual.Api.Controllers;

[ApiController]
public sealed class EventsController(IMediator mediator) : ControllerBase
{
    [HttpPost("/events")]
    public async Task<ActionResult<EventDetailsResponse>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AcceptEventCommand(request), cancellationToken);
        return result switch
        {
            IntakeResult.Invalid => BadRequest(new ErrorResponse("External id is required and must be at most 128 characters.")),
            IntakeResult.UserNotFound => NotFound(new ErrorResponse("User was not found.")),
            IntakeResult.UsersUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse("Users service is unavailable.")),
            IntakeResult.Conflict => Conflict(new ErrorResponse("Event external id already exists with a different payload.")),
            IntakeResult.Created created => Created($"/events/{Uri.EscapeDataString(created.Event.ExternalId)}", EventMapper.ToDetails(created.Event)),
            IntakeResult.Existing existing => Ok(EventMapper.ToDetails(existing.Event)),
            IntakeResult.AcceptedPending pending => StatusCode(StatusCodes.Status202Accepted, EventMapper.ToDetails(pending.Event)),
            _ => throw new InvalidOperationException($"Unexpected intake result {result.GetType().Name}.")
        };
    }

    [HttpGet("/events/{externalId}")]
    public async Task<ActionResult<EventDetailsResponse>> Get(string externalId, CancellationToken cancellationToken)
    {
        var ev = await mediator.Send(new GetEventQuery(externalId), cancellationToken);
        return ev is null ? NotFound(new ErrorResponse("Event was not found.")) : Ok(EventMapper.ToDetails(ev));
    }

    [HttpGet("/users/{userExternalId}/events")]
    public async Task<ActionResult<IReadOnlyList<EventSummaryResponse>>> List(string userExternalId, CancellationToken cancellationToken)
    {
        if (!ExternalIds.IsValid(userExternalId))
            return BadRequest(new ErrorResponse("External id is required and must be at most 128 characters."));

        var eventsForUser = await mediator.Send(new ListUserEventsQuery(userExternalId), cancellationToken);
        return Ok(eventsForUser.Select(EventMapper.ToSummary).ToList());
    }
}

[ApiController]
[Route("schema")]
public sealed class SchemaController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<SchemaResponse> Get(CancellationToken cancellationToken) =>
        new(await mediator.Send(new GetSchemaQuery(), cancellationToken));

    [HttpPut]
    public async Task<SchemaResponse> Put(SetSchemaRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !Enum.IsDefined(request.SchemaType))
            throw new BadHttpRequestException("Schema type is required.");

        return new SchemaResponse(await mediator.Send(new SetSchemaCommand(request.SchemaType), cancellationToken));
    }
}

[ApiController]
public sealed class ClaimsController(IMediator mediator) : ControllerBase
{
    [HttpPost("/internal/payouts/claim")]
    public async Task<ClaimPayoutResponse> Claim(ClaimPayoutRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.PayoutId == Guid.Empty)
            throw new BadHttpRequestException("Payout id is required.");

        return await mediator.Send(new ClaimPayoutCommand(request), cancellationToken);
    }
}
