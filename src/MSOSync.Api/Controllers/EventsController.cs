using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class EventsController(
    IEventQueryService      events,
    IValidator<EventFilter> validator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] EventFilter filter, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await events.GetEventsAsync(filter, ct));
    }

    [HttpGet("{eventId:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetEventById(long eventId, CancellationToken ct)
    {
        var dto = await events.GetEventByIdAsync(eventId, ct);
        if (dto is null) throw new NotFoundException($"Event {eventId} not found.");
        return Ok(dto);
    }
}
