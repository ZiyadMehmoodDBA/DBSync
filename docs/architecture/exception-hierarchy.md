# Exception Hierarchy

All unhandled exceptions are processed by `GlobalExceptionHandler`
in `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs` (ASP.NET Core
`IExceptionHandler`).

All domain exceptions derive from the abstract base
`MSOSync.Common.Exceptions.SyncException(string message, string code)`,
which carries a machine-readable `Code` alongside the human-readable message.

## Domain Exception → HTTP Status Mapping

| Exception Type | HTTP Status | Default Error Code |
|---|---|---|
| `NotFoundException` | 404 Not Found | `NOT_FOUND` |
| `DuplicateEntityException` | 409 Conflict | `DUPLICATE_ENTITY` |
| `ConflictException` | 409 Conflict | `CONFLICT` |
| `ConcurrencyException` | 409 Conflict | `CONCURRENCY_CONFLICT` |
| `InvalidLifecycleTransitionException` | 409 Conflict | `INVALID_LIFECYCLE_TRANSITION` (special body, see below) |
| `ValidationException` (MSOSync.Common) | 400 Bad Request | `VALIDATION_ERROR` |
| `ValidationException` (FluentValidation) | 400 Bad Request | `VALIDATION_ERROR` (joined error messages) |
| `ForbiddenOperationException` | 403 Forbidden | `FORBIDDEN` |
| `UnauthorizedException` | 401 Unauthorized | `UNAUTHORIZED` |
| Catch-all (`Exception`) | 500 Internal Server Error | `INTERNAL_SERVER_ERROR` |

`SyncException` subtypes accept an optional `code` parameter, so a throw site
may override the default code shown above. The handler always emits `ex.Code`.

### Special case: `InvalidLifecycleTransitionException`

Returns 409 with a structured body defined by the node lifecycle spec (§7.4):

```json
{
  "code": "INVALID_LIFECYCLE_TRANSITION",
  "from": "Pending",
  "requested": "Decommissioned",
  "allowedTransitions": ["Approved", "Rejected"],
  "correlationId": "8f9c…"
}
```

## Response Format

All other error responses use the standard error shape
(see `docs/architecture/api-response-contract.md`):

```json
{
  "timestamp": "2026-07-17T12:00:00Z",
  "status": 404,
  "error": "Not Found",
  "code": "NOT_FOUND",
  "message": "Node 'abc' was not found.",
  "correlationId": "abc123"
}
```

`correlationId` echoes the request's `X-Correlation-Id` header when present,
otherwise falls back to `HttpContext.TraceIdentifier`. The handler also writes
the `X-Correlation-Id` **response header** on every error response (RULE-ERR-3).

## Tenant Access Errors (middleware boundary)

`TenantAccessException` (`MSOSync.Security.Tenancy`) is **not** routed through
`GlobalExceptionHandler`. It is thrown by `TenantResolver` /
`TenantAccessValidator` and caught by `TenantResolverMiddleware`, which runs
before the MVC pipeline and writes `{ "error": "<message>" }` with the status
code carried by the exception (401 / 403 / 409). This is a legacy wire shape
consumed by existing clients (see Legacy Error Shapes in
`api-response-contract.md`).

## Controller Try/Catch Policy

Controller try/catch blocks are permitted only for (RULE-ERR-1):

- `JsonException` — malformed user-supplied JSON (e.g., filter payloads)
- `OperationCanceledException` — request cancellation
- Boundary guards on raw request bodies (e.g., `SyncController.Push`
  decompress/deserialize `catch (Exception)` → 400, accepted under 2A-021)
- BCL exceptions with specific semantics not covered by the global handler
  (e.g., `OperationsController` catching `KeyNotFoundException` /
  `InvalidOperationException` from the operations engine,
  `UsersController.CreateUser` catching `InvalidOperationException`
  filtered on "already taken")

Controllers must NOT catch any `SyncException` subtype
(`NotFoundException`, `DuplicateEntityException`, `ConflictException`,
`ValidationException`, `ForbiddenOperationException`, `ConcurrencyException`,
`UnauthorizedException`, `InvalidLifecycleTransitionException`).
Those are GlobalExceptionHandler's responsibility.

## Adding New Domain Exceptions

1. Create the exception class in `MSOSync.Common` (derive from `SyncException`)
   or the appropriate domain project.
2. Add a `case` arm in `GlobalExceptionHandler` with the correct HTTP status
   and error code (RULE-ERR-2: register before shipping).
3. Write a unit test verifying the mapping.
4. Update this document.
