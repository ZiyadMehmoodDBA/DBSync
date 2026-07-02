using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class EventsController(
    IEventQueryService              events,
    IValidator<EventFilter>         validator,
    IExportService<EventFilter>     exporter,
    IExportAuditService             exportAudit) : ControllerBase
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

    [HttpGet("export")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> ExportEvents(
        [FromQuery] EventFilter filter,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return new MSOSync.Api.Results.StreamingExportResult(
            isJson
                ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
                : (s, t) => exporter.ExportCsvAsync(s, filter, t),
            isJson ? "application/json" : "text/csv",
            isJson ? $"events-{date}.json" : $"events-{date}.csv",
            (rows, ms) => exportAudit.WriteAsync("events", format, rows, ms, ct));
    }
}
