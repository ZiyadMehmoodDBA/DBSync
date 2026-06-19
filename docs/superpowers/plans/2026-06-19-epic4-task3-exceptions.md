# Epic 4 / Task 3: Exception Hierarchy + GlobalExceptionHandler

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `SyncException` hierarchy (6 concrete types) to `MSOSync.Common/Exceptions/` and implement `GlobalExceptionHandler` in `MSOSync.Api/Exceptions/` that maps them to a standard error envelope. Wire into `Program.cs`.

**Architecture:** Exceptions carry a `Code` string alongside `Message`. The handler returns `{ timestamp, status, error, code, message, correlationId }`. Unknown exceptions return 500 with generic message and NO stack trace in the body. `X-Correlation-Id` header is used if present, otherwise `TraceIdentifier`.

**Tech Stack:** ASP.NET Core 9 `IExceptionHandler` (not legacy `UseExceptionHandler` middleware), `ProblemDetails`

## Global Constraints

- Exception types in `MSOSync.Common.Exceptions` namespace
- Handler in `MSOSync.Api.Exceptions` namespace
- 500 responses: log at Error level; body contains ONLY `"INTERNAL_SERVER_ERROR"` — no `InnerException`, no `StackTrace`
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Common/Exceptions/SyncException.cs`
- Create: `src/MSOSync.Common/Exceptions/NotFoundException.cs`
- Create: `src/MSOSync.Common/Exceptions/DuplicateEntityException.cs`
- Create: `src/MSOSync.Common/Exceptions/ValidationException.cs`
- Create: `src/MSOSync.Common/Exceptions/ForbiddenOperationException.cs`
- Create: `src/MSOSync.Common/Exceptions/ConcurrencyException.cs`
- Create: `src/MSOSync.Common/Exceptions/UnauthorizedException.cs`
- Create: `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`
- Modify: `src/MSOSync.App/Program.cs` — register handler + add `app.UseExceptionHandler()`

**Interfaces:**
- Consumes: nothing from prior tasks
- Produces: `SyncException` hierarchy + `GlobalExceptionHandler` — consumed by all services (Tasks 4–7) and wiring (Task 8)

---

- [ ] **Step 1: Create `SyncException` base class**

```csharp
// src/MSOSync.Common/Exceptions/SyncException.cs
namespace MSOSync.Common.Exceptions;

public abstract class SyncException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
```

- [ ] **Step 2: Create the six concrete exception types**

```csharp
// src/MSOSync.Common/Exceptions/NotFoundException.cs
namespace MSOSync.Common.Exceptions;

public sealed class NotFoundException(string message, string code = "NOT_FOUND")
    : SyncException(message, code);
```

```csharp
// src/MSOSync.Common/Exceptions/DuplicateEntityException.cs
namespace MSOSync.Common.Exceptions;

public sealed class DuplicateEntityException(string message, string code = "DUPLICATE_ENTITY")
    : SyncException(message, code);
```

```csharp
// src/MSOSync.Common/Exceptions/ValidationException.cs
namespace MSOSync.Common.Exceptions;

public sealed class ValidationException(string message, string code = "VALIDATION_ERROR")
    : SyncException(message, code);
```

```csharp
// src/MSOSync.Common/Exceptions/ForbiddenOperationException.cs
namespace MSOSync.Common.Exceptions;

public sealed class ForbiddenOperationException(string message, string code = "FORBIDDEN")
    : SyncException(message, code);
```

```csharp
// src/MSOSync.Common/Exceptions/ConcurrencyException.cs
namespace MSOSync.Common.Exceptions;

public sealed class ConcurrencyException(string message, string code = "CONCURRENCY_CONFLICT")
    : SyncException(message, code);
```

```csharp
// src/MSOSync.Common/Exceptions/UnauthorizedException.cs
namespace MSOSync.Common.Exceptions;

public sealed class UnauthorizedException(string message, string code = "UNAUTHORIZED")
    : SyncException(message, code);
```

- [ ] **Step 3: Create `GlobalExceptionHandler` in `MSOSync.Api/Exceptions/`**

Create directory `src/MSOSync.Api/Exceptions/` if it does not exist, then create the file:

```csharp
// src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs
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
            NotFoundException ex            => (404, "Not Found",            ex.Code, ex.Message),
            DuplicateEntityException ex     => (409, "Conflict",             ex.Code, ex.Message),
            ValidationException ex          => (400, "Bad Request",          ex.Code, ex.Message),
            ForbiddenOperationException ex  => (403, "Forbidden",            ex.Code, ex.Message),
            ConcurrencyException ex         => (409, "Conflict",             ex.Code, ex.Message),
            UnauthorizedException ex        => (401, "Unauthorized",         ex.Code, ex.Message),
            _                               => (500, "Internal Server Error","INTERNAL_SERVER_ERROR", "An unexpected error occurred")
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

- [ ] **Step 4: Register handler and add middleware in `Program.cs`**

In `src/MSOSync.App/Program.cs`, add registrations after `builder.Services.AddProblemDetails()` (if present) or after `builder.Services.AddSwaggerGen()`:

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
```

Also add the using at the top of the file:

```csharp
using MSOSync.Api.Exceptions;
```

In the pipeline, add `app.UseExceptionHandler()` as the FIRST middleware call (before `app.UseSecurityHeaders()`):

```csharp
app.UseExceptionHandler();
app.UseSecurityHeaders();
app.UseAuthentication();
app.UseNodeTokenAuth();
app.UseAuthorization();
```

- [ ] **Step 5: Build full solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 6: Commit**

```powershell
git add src/MSOSync.Common/Exceptions/SyncException.cs
git add src/MSOSync.Common/Exceptions/NotFoundException.cs
git add src/MSOSync.Common/Exceptions/DuplicateEntityException.cs
git add src/MSOSync.Common/Exceptions/ValidationException.cs
git add src/MSOSync.Common/Exceptions/ForbiddenOperationException.cs
git add src/MSOSync.Common/Exceptions/ConcurrencyException.cs
git add src/MSOSync.Common/Exceptions/UnauthorizedException.cs
git add src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs
git add src/MSOSync.App/Program.cs
git commit -m "feat(common,api): add SyncException hierarchy and GlobalExceptionHandler"
```
