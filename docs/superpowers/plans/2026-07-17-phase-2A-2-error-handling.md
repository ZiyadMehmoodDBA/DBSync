# Phase 2A.2 — Error Handling

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify the exception pipeline is complete and consistent. Audit found the error handling to be well-centralized with no critical gaps. This plan performs a final verification scan, documents the exception hierarchy, and commits that document as a Phase 2A deliverable.

**Architecture:** `GlobalExceptionHandler` at `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs` implements `IExceptionHandler` and maps all known domain exceptions to standardized `ProblemDetails` JSON. All controllers that have try/catch blocks use them for non-domain-exception cases (e.g., `JsonException` for filter JSON, `InvalidOperationException` for specific conflict cases). These are legitimate — they do not duplicate the global handler.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core `IExceptionHandler` / RFC 7807 ProblemDetails

## Global Constraints

- No new product features. Scope is strictly verification and documentation.
- Definition of Complete: verification scan passed + docs committed + `dotnet test` exits 0.
- RULE-ERR-1: No controller catches an exception type already handled by `GlobalExceptionHandler`.
- RULE-ERR-2: Every new domain exception type is registered in `GlobalExceptionHandler` before shipping.
- RULE-ERR-3: `X-Correlation-Id` is present on all 4xx and 5xx responses.

---

## File Map

**Create:**
- `docs/architecture/exception-hierarchy.md`

**Possibly Modify:**
- `src/MSOSync.Api/Controllers/*.cs` — only if the scan finds redundant catches

---

## Task 1: Verify GlobalExceptionHandler Coverage

**Files:**
- Read: `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`

- [ ] **Step 1: Read GlobalExceptionHandler**

```
cat D:\MSOSync\src\MSOSync.Api\Exceptions\GlobalExceptionHandler.cs
```

Identify every exception type it handles and the HTTP status code it maps each to. List them.

- [ ] **Step 2: List all domain exception types in the solution**

```
grep -rn "class.*Exception.*:" D:\MSOSync\src\ --include="*.cs" | grep -v "catch\|throw\|//\|test"
```

Cross-reference against GlobalExceptionHandler. Every domain exception type should appear there. If any are missing, add them to GlobalExceptionHandler before marking this workstream complete.

- [ ] **Step 3: Scan controller try/catch blocks**

```
grep -rn "catch" D:\MSOSync\src\MSOSync.Api\Controllers\ --include="*.cs" -B 2 -A 3
```

For each catch block found, verify:
- It does NOT catch an exception type that GlobalExceptionHandler already handles.
- If it does catch a duplicate type, remove the controller-level catch and let GlobalExceptionHandler handle it.

Expected result based on initial audit: All controller catches are for `JsonException` (malformed filter JSON), `OperationCanceledException`, or very specific `InvalidOperationException` cases not covered by the global handler. These are acceptable.

- [ ] **Step 4: Verify X-Correlation-Id in error responses**

Check GlobalExceptionHandler writes the correlation ID header:
```
grep -n "Correlation\|correlationId\|X-Correlation" D:\MSOSync\src\MSOSync.Api\Exceptions\GlobalExceptionHandler.cs
```

Expected: Header is set in the handler. If missing, add it.

---

## Task 2: Write Exception Hierarchy Document

**Files:**
- Create: `docs/architecture/exception-hierarchy.md`

- [ ] **Step 1: Create exception-hierarchy.md**

After completing the scan in Task 1, create `docs/architecture/exception-hierarchy.md` with the actual exception types and their HTTP mappings:

```markdown
# Exception Hierarchy

All unhandled exceptions are processed by `GlobalExceptionHandler`
in `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs`.

## Domain Exception → HTTP Status Mapping

| Exception Type | HTTP Status | Error Code |
|---|---|---|
| `NotFoundException` | 404 Not Found | NOT_FOUND |
| `DuplicateEntityException` | 409 Conflict | DUPLICATE |
| `ValidationException` (FluentValidation) | 400 Bad Request | VALIDATION_ERROR |
| `ForbiddenOperationException` | 403 Forbidden | FORBIDDEN |
| `ConcurrencyException` | 409 Conflict | CONCURRENCY_CONFLICT |
| `UnauthorizedException` | 401 Unauthorized | UNAUTHORIZED |
| Catch-all (`Exception`) | 500 Internal Server Error | INTERNAL_ERROR |

*Note: Update this table from actual GlobalExceptionHandler code during plan execution.*

## Response Format

All error responses use RFC 7807 ProblemDetails:
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
Response includes `X-Correlation-Id` header.

## Controller Try/Catch Policy

Controller try/catch blocks are permitted only for:
- `JsonException` — malformed user-supplied JSON (e.g., filter payloads)
- `OperationCanceledException` — request cancellation
- Domain-specific `InvalidOperationException` not covered by GlobalExceptionHandler

Controllers must NOT catch: `NotFoundException`, `DuplicateEntityException`,
`ValidationException`, `ForbiddenOperationException`, `ConcurrencyException`, or `UnauthorizedException`.
Those are GlobalExceptionHandler's responsibility.

## Adding New Domain Exceptions

1. Create the exception class in `MSOSync.Common` or the appropriate domain project.
2. Add a `case` handler in `GlobalExceptionHandler` with the correct HTTP status and error code.
3. Write a unit test verifying the mapping.
4. Update this document.
```

- [ ] **Step 2: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 3: Commit**

```
git add docs/architecture/exception-hierarchy.md
git commit -m "docs(2A.2): exception hierarchy and error handling contract"
```

---

## Completion Criteria

2A.2 is **Complete** when:
1. No controller catches a type already handled by `GlobalExceptionHandler`.
2. All domain exception types appear in `GlobalExceptionHandler`.
3. `X-Correlation-Id` is written on all error responses.
4. `dotnet test` exits 0.
5. `docs/architecture/exception-hierarchy.md` committed with accurate mapping table.
