# API Response Contract

MSOSync uses a **bare-resource** response pattern. Controllers return
the DTO directly — there is no `ApiResponse<T>` envelope wrapper.

## Status Code Mapping

| Status | When Used | Body |
|---|---|---|
| 200 OK | Successful GET, PUT | Resource DTO or collection |
| 201 Created | Resource created synchronously | Created resource DTO |
| 202 Accepted | Async operation enqueued | `{ id }` of created job |
| 204 No Content | DELETE, bulk PUT | Empty |
| 400 Bad Request | Validation failure, invalid input | `ProblemDetails` |
| 401 Unauthorized | Missing or invalid JWT | `ProblemDetails` |
| 403 Forbidden | Insufficient permissions | `ProblemDetails` |
| 404 Not Found | Resource does not exist | `ProblemDetails` |
| 409 Conflict | Duplicate entity, concurrency conflict | `ProblemDetails` |
| 500 Internal Server Error | Unhandled exception | `ProblemDetails` |

## Error Responses

All error responses use RFC 7807 `ProblemDetails` format, produced by
`GlobalExceptionHandler` in `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`.

Error response structure:
```json
{
  "timestamp": "2026-07-17T12:00:00Z",
  "status": 400,
  "error": "Validation Failed",
  "code": "VALIDATION_ERROR",
  "message": "NodeId is required",
  "correlationId": "abc123"
}
```

All 4xx and 5xx responses include the `X-Correlation-Id` response header.

## Legacy Error Shapes (wire contracts preserved)

A small set of endpoints predate the ProblemDetails convention and return
named error DTOs from `src/MSOSync.Api/Dtos/Common/`. Their JSON shapes are
consumed by existing clients and must not change:

| DTO | Shape | Used by |
|---|---|---|
| `CodeResponse` | `{ "code": "SEQUENCE_GAP" }` | `SyncController.Push` 409 — parsed by `AcknowledgementService` |
| `CodeMessageResponse` | `{ "code", "message" }` | `BatchController` retry 409s (`INVALID_TRANSITION`, `LOCK_UNAVAILABLE`) |
| `ErrorResponse` | `{ "error" }` | Auth 401s, Operations 404/409, Plugin 503s, Users 409, Nodes heartbeat 400, Notification 400s |
| `MessageResponse` | `{ "message" }` | `AuditController.ExportCorrelationAsync` 501/400 |

New endpoints must not add to this list — use `ProblemDetails` via exceptions.

## Rules

- **RULE-API-1:** Every controller action has `[ProducesResponseType]` for each possible status code.
- **RULE-API-2:** No anonymous objects in controller responses. All response bodies use named record or class types. (`CreatedAtAction` route-value objects are not response bodies and are exempt.)
- **RULE-API-3:** Error responses use `ProblemDetails` format via `GlobalExceptionHandler`. No manual error JSON construction.
