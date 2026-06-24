# Task 6: Controllers + GlobalExceptionHandler

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Create three thin controllers (Events, IncomingBatches, BatchErrors) and add a `FluentValidation.ValidationException` handler arm to `GlobalExceptionHandler`. **The `ViewerOrAbove` policy already exists** in `SecurityServiceExtensions` — do not redefine it.

**Files:**
- Create: `src/MSOSync.Api/Controllers/EventsController.cs`
- Create: `src/MSOSync.Api/Controllers/IncomingBatchesController.cs`
- Create: `src/MSOSync.Api/Controllers/BatchErrorsController.cs`
- Modify: `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`

**Note:** DI registration for the query services and validators is done in Task 7. The controllers compile now; integration tests run after Task 7.

**Interfaces:**
- Consumes (Tasks 1–5): `EventFilter`, `IncomingBatchFilter`, `BatchErrorFilter`, `IEventQueryService`, `IIncomingBatchQueryService`, `IBatchErrorQueryService`; `IValidator<TFilter>` (FluentValidation)
- Consumes: `NotFoundException` from `MSOSync.Common.Exceptions`
- Produces: HTTP endpoints at routes documented below

---

- [ ] **Step 1: Add FluentValidation.ValidationException arm to GlobalExceptionHandler**

Open `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`.

Current content:
```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;

namespace MSOSync.Api.Exceptions;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct)
    {
        var (status, error, code, message) = exception switch
        {
            NotFoundException ex           => (404, "Not Found",            ex.Code, ex.Message),
            DuplicateEntityException ex    => (409, "Conflict",             ex.Code, ex.Message),
            ValidationException ex         => (400, "Bad Request",          ex.Code, ex.Message),
            ForbiddenOperationException ex => (403, "Forbidden",            ex.Code, ex.Message),
            ConcurrencyException ex        => (409, "Conflict",             ex.Code, ex.Message),
            UnauthorizedException ex       => (401, "Unauthorized",         ex.Code, ex.Message),
            _                              => (500, "Internal Server Error", "INTERNAL_SERVER_ERROR", "An unexpected error occurred")
        };
        ...
    }
}
```

Add a `using FluentValidation;` at the top and insert a new arm for `FluentValidation.ValidationException`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;

namespace MSOSync.Api.Exceptions;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct)
    {
        var (status, error, code, message) = exception switch
        {
            NotFoundException ex                   => (404, "Not Found",            ex.Code,       ex.Message),
            DuplicateEntityException ex            => (409, "Conflict",             ex.Code,       ex.Message),
            ValidationException ex                 => (400, "Bad Request",          ex.Code,       ex.Message),
            ForbiddenOperationException ex         => (403, "Forbidden",            ex.Code,       ex.Message),
            ConcurrencyException ex                => (409, "Conflict",             ex.Code,       ex.Message),
            UnauthorizedException ex               => (401, "Unauthorized",         ex.Code,       ex.Message),
            FluentValidation.ValidationException e => (400, "Bad Request",          "VALIDATION_ERROR",
                string.Join("; ", e.Errors.Select(x => x.ErrorMessage))),
            _                                      => (500, "Internal Server Error", "INTERNAL_SERVER_ERROR",
                "An unexpected error occurred")
        };

        if (status == 500)
            logger.LogError(exception, "Unhandled exception");

        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            timestamp = DateTime.UtcNow,
            status,
            error,
            code,
            message,
            correlationId
        }, ct);

        return true;
    }
}
```

- [ ] **Step 2: Verify GlobalExceptionHandler builds**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src\MSOSync.Api -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Create EventsController**

Create `src/MSOSync.Api/Controllers/EventsController.cs`:

```csharp
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
    IEventQueryService     events,
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
```

- [ ] **Step 4: Create IncomingBatchesController**

Create `src/MSOSync.Api/Controllers/IncomingBatchesController.cs`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.IncomingBatches;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/incoming-batches")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class IncomingBatchesController(
    IIncomingBatchQueryService    batches,
    IValidator<IncomingBatchFilter> validator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetIncomingBatches(
        [FromQuery] IncomingBatchFilter filter, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await batches.GetIncomingBatchesAsync(filter, ct));
    }

    [HttpGet("{batchId:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetIncomingBatchById(long batchId, CancellationToken ct)
    {
        var dto = await batches.GetIncomingBatchByIdAsync(batchId, ct);
        if (dto is null) throw new NotFoundException($"IncomingBatch {batchId} not found.");
        return Ok(dto);
    }
}
```

- [ ] **Step 5: Create BatchErrorsController**

Create `src/MSOSync.Api/Controllers/BatchErrorsController.cs`:

```csharp
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
    IBatchErrorQueryService     errors,
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
```

- [ ] **Step 6: Verify full solution builds**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings. The controllers reference `IEventQueryService` etc. which are in `MSOSync.Metadata`. Check that `MSOSync.Api.csproj` already references `MSOSync.Metadata` — it should from prior epics.

If the build fails with "cannot find MSOSync.Metadata.Events", open `src/MSOSync.Api/MSOSync.Api.csproj` and verify `<ProjectReference Include="..\MSOSync.Metadata\MSOSync.Metadata.csproj" />` is present.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Api/Controllers/EventsController.cs
git add src/MSOSync.Api/Controllers/IncomingBatchesController.cs
git add src/MSOSync.Api/Controllers/BatchErrorsController.cs
git add src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs
git commit -m "feat(9a): add EventsController, IncomingBatchesController, BatchErrorsController; handle FluentValidation in GlobalExceptionHandler"
```
