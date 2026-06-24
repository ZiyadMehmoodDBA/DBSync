using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.BatchErrors;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/batch-errors")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class BatchErrorsController(
    IBatchErrorQueryService      errors,
    IValidator<BatchErrorFilter> validator) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetBatchErrorSummary(
        [FromQuery] long?     batchId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        return Ok(await errors.GetBatchErrorSummaryAsync(batchId, from, to, ct));
    }

    [HttpGet("{errorId:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetBatchErrorById(long errorId, CancellationToken ct)
    {
        var dto = await errors.GetBatchErrorByIdAsync(errorId, ct);
        if (dto is null) throw new NotFoundException($"BatchError {errorId} not found.");
        return Ok(dto);
    }

    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetBatchErrors(
        [FromQuery] BatchErrorFilter filter, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await errors.GetBatchErrorsAsync(filter, ct));
    }
}
