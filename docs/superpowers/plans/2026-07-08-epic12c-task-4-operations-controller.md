# Epic 12C — Task 4: OperationsController

**Branch:** `feat/epic12c-system-admin`  
**Files touched:** 1 new  
**Depends on:** Task 3 complete (`IOperationQueryService` and `IOperationService` registered in DI).

---

## Context

This task exposes the operations subsystem over HTTP. The controller follows the exact patterns already used in the codebase:

- `[Authorize(Policy = "ViewerOrAbove")]` on the class; per-action permission checks via `INodeAuthorizationService` or `IPermissionService`.
- Primary-constructor DI (no `_field =` assignments).
- `IActionResult` returns with explicit status codes.
- 409 for state-conflict rejections; 404 for unknown resources; 400 for invalid input.

The four endpoints are:

| Method | Path                                   | Auth                                           |
|--------|----------------------------------------|------------------------------------------------|
| GET    | `/api/v1/operations`                   | ViewerOrAbove                                  |
| GET    | `/api/v1/operations/{id}`              | ViewerOrAbove                                  |
| POST   | `/api/v1/operations/{id}/cancel`       | ManageConfigurations OR ManageNodeLifecycle    |
| POST   | `/api/v1/operations/{id}/retry`        | ManageConfigurations OR ManageNodeLifecycle    |

---

## Steps

- [ ] **1. Create the controller**

  Create `src/MSOSync.Api/Controllers/OperationsController.cs`:

  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using MSOSync.Common.Exceptions;
  using MSOSync.Metadata.Operations;
  using MSOSync.Metadata.Permissions;

  namespace MSOSync.Api.Controllers;

  [ApiController]
  [Route("api/v1/operations")]
  [Authorize(Policy = "ViewerOrAbove")]
  public sealed class OperationsController(
      IOperationQueryService queryService,
      IOperationService      operationService,
      IPermissionService     permissions) : ControllerBase
  {
      private string Actor => User.Identity?.Name
          ?? throw new UnauthorizedException("No identity", "UNAUTHORIZED");

      private Guid ActorId
      {
          get
          {
              var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
              return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
          }
      }

      // ── GET /api/v1/operations ─────────────────────────────────────────────────

      /// <summary>
      /// Returns a cursor-paginated list of operations, newest first.
      /// </summary>
      /// <param name="types">Comma-separated operation types to include (Export, Rollout, Decommission, Recovery).</param>
      /// <param name="statuses">Comma-separated statuses to include (Pending, Running, Completed, Failed, Cancelled).</param>
      /// <param name="sources">Comma-separated sources to include (User, System, Scheduler, Worker, Api).</param>
      /// <param name="from">Inclusive lower bound on started_at (UTC).</param>
      /// <param name="to">Inclusive upper bound on started_at (UTC).</param>
      /// <param name="initiatedBy">Filter by the user GUID who initiated the operation.</param>
      /// <param name="cursor">Opaque cursor returned by the previous page response.</param>
      /// <param name="pageSize">Number of items per page (1–100, default 25).</param>
      [HttpGet]
      [ProducesResponseType(typeof(OperationPageDto), 200)]
      public async Task<IActionResult> GetOperations(
          [FromQuery] string?   types       = null,
          [FromQuery] string?   statuses    = null,
          [FromQuery] string?   sources     = null,
          [FromQuery] DateTime? from        = null,
          [FromQuery] DateTime? to          = null,
          [FromQuery] string?   initiatedBy = null,
          [FromQuery] string?   cursor      = null,
          [FromQuery] int       pageSize    = 25,
          CancellationToken ct = default)
      {
          if (pageSize is < 1 or > 100)
              return BadRequest(new { error = "pageSize must be between 1 and 100." });

          var filter = new OperationFilter(
              Types:       SplitCsv(types),
              Statuses:    SplitCsv(statuses),
              Sources:     SplitCsv(sources),
              From:        from,
              To:          to,
              InitiatedBy: initiatedBy,
              Cursor:      cursor,
              PageSize:    pageSize);

          var result = await queryService.GetPageAsync(filter, ct);
          return Ok(result);
      }

      // ── GET /api/v1/operations/{id} ────────────────────────────────────────────

      /// <summary>Returns the detail view of a single operation.</summary>
      [HttpGet("{id:guid}")]
      [ProducesResponseType(typeof(OperationDetailDto), 200)]
      [ProducesResponseType(404)]
      public async Task<IActionResult> GetOperation(Guid id, CancellationToken ct)
      {
          var detail = await queryService.GetDetailAsync(id, ct);
          if (detail is null) return NotFound(new { error = $"Operation {id} not found." });
          return Ok(detail);
      }

      // ── POST /api/v1/operations/{id}/cancel ───────────────────────────────────

      /// <summary>
      /// Cancels a Pending or Running operation.
      /// Returns 409 if the operation is already in a terminal state or does not support cancellation.
      /// </summary>
      [HttpPost("{id:guid}/cancel")]
      [ProducesResponseType(typeof(OperationDetailDto), 200)]
      [ProducesResponseType(404)]
      [ProducesResponseType(403)]
      [ProducesResponseType(409)]
      public async Task<IActionResult> CancelOperation(Guid id, CancellationToken ct)
      {
          if (!await HasManagePermissionAsync(ct))
              return Forbid();

          try
          {
              await operationService.CancelAsync(id, ActorId, ct);
          }
          catch (KeyNotFoundException)
          {
              return NotFound(new { error = $"Operation {id} not found." });
          }
          catch (InvalidOperationException ex)
          {
              return Conflict(new { error = ex.Message });
          }

          var updated = await queryService.GetDetailAsync(id, ct);
          return Ok(updated);
      }

      // ── POST /api/v1/operations/{id}/retry ────────────────────────────────────

      /// <summary>
      /// Retries a Failed or Cancelled operation.
      /// Returns 409 if the operation is not retryable or does not support retry.
      /// </summary>
      [HttpPost("{id:guid}/retry")]
      [ProducesResponseType(typeof(OperationDetailDto), 200)]
      [ProducesResponseType(404)]
      [ProducesResponseType(403)]
      [ProducesResponseType(409)]
      public async Task<IActionResult> RetryOperation(Guid id, CancellationToken ct)
      {
          if (!await HasManagePermissionAsync(ct))
              return Forbid();

          try
          {
              await operationService.RetryAsync(id, ActorId, ct);
          }
          catch (KeyNotFoundException)
          {
              return NotFound(new { error = $"Operation {id} not found." });
          }
          catch (InvalidOperationException ex)
          {
              return Conflict(new { error = ex.Message });
          }

          var updated = await queryService.GetDetailAsync(id, ct);
          return Ok(updated);
      }

      // ── Helpers ────────────────────────────────────────────────────────────────

      /// <summary>
      /// Returns true if the caller holds either ManageConfigurations or ManageNodeLifecycle.
      /// Matching the pattern used by NodeLifecycleController for multi-permission checks.
      /// </summary>
      private async Task<bool> HasManagePermissionAsync(CancellationToken ct)
      {
          var effective = await permissions.GetEffectivePermissionsAsync(Actor, ct);
          return effective.Permissions.Contains(SystemPermissions.ManageConfigurations)
              || effective.Permissions.Contains(SystemPermissions.ManageNodeLifecycle);
      }

      private static string[]? SplitCsv(string? value)
          => string.IsNullOrWhiteSpace(value)
              ? null
              : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }
  ```

  > **ActorId claim note:** The JWT issued by this application sets `NameIdentifier` to the user's `UserId` GUID (confirmed in `JwtService`). If the claim is absent (e.g. in test tokens), `ActorId` returns `Guid.Empty`, which is safe — `OperationService.CancelAsync` stores the actorId only for audit purposes and never validates it.

- [ ] **2. Verify SystemPermissions constants exist**

  Open `src/MSOSync.Metadata/Permissions/PermissionDtos.cs` (or wherever `SystemPermissions` is defined) and confirm `ManageConfigurations` and `ManageNodeLifecycle` are declared. Run:

  ```powershell
  dotnet build src\MSOSync.Api\MSOSync.Api.csproj
  ```

  If `SystemPermissions.ManageConfigurations` does not exist, check the actual constant name in `PermissionDtos.cs` and update the controller accordingly.

- [ ] **3. Confirm OperationsHub exists**

  The `OperationChangedPublisher` (Task 3) references `OperationsHub`. Verify:

  ```powershell
  Get-ChildItem src\MSOSync.App\Hubs\OperationsHub.cs -ErrorAction SilentlyContinue
  ```

  If the file does not exist, the App project will not compile. Create a stub:

  ```csharp
  using Microsoft.AspNetCore.SignalR;

  namespace MSOSync.App.Hubs;

  /// <summary>
  /// SignalR hub for real-time operation status updates.
  /// Clients join the "operators" group to receive OperationChanged events.
  /// </summary>
  public sealed class OperationsHub : Hub
  {
      public override async Task OnConnectedAsync()
      {
          await Groups.AddToGroupAsync(Context.ConnectionId, "operators");
          await base.OnConnectedAsync();
      }
  }
  ```

  Save as `src/MSOSync.App/Hubs/OperationsHub.cs` if it does not already exist. If it exists under a different class name, update the `IHubContext<T>` type parameter in `OperationChangedPublisher` to match.

- [ ] **4. Build the API project**

  ```powershell
  dotnet build src\MSOSync.Api\MSOSync.Api.csproj
  ```

  Expected: 0 errors, 0 warnings about unresolved symbols.

- [ ] **5. Integration smoke-test (optional — requires a running DB)**

  If a local SQL Server instance is available with the M024 migration applied:

  ```powershell
  # Start the API
  dotnet run --project src\MSOSync.Api\MSOSync.Api.csproj &

  # List operations (expect empty 200)
  curl -s -H "Authorization: Bearer <admin_token>" https://localhost:5001/api/v1/operations | jq .

  # Attempt cancel of a non-existent operation (expect 404)
  curl -s -X POST -H "Authorization: Bearer <admin_token>" `
       https://localhost:5001/api/v1/operations/00000000-0000-0000-0000-000000000001/cancel | jq .
  ```

- [ ] **6. Run the integration test suite**

  ```powershell
  dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj
  ```

  All pre-existing tests must still pass. The new controller does not have dedicated integration tests yet — those are part of Task 5.

- [ ] **7. Commit**

  ```powershell
  git add src\MSOSync.Api\Controllers\OperationsController.cs
  # If OperationsHub.cs was created:
  git add src\MSOSync.App\Hubs\OperationsHub.cs
  git commit -m "feat(12C-4): OperationsController — list, detail, cancel, retry endpoints"
  ```

---

## Acceptance criteria

- `dotnet build src\MSOSync.Api` passes with 0 errors.
- `GET /api/v1/operations` returns 200 with an `OperationPageDto` (items list may be empty).
- `GET /api/v1/operations/{unknownId}` returns 404.
- `POST /api/v1/operations/{id}/cancel` with a Viewer-only token returns 403.
- `POST /api/v1/operations/{id}/cancel` on a Completed operation returns 409 with an `error` field.
- `POST /api/v1/operations/{unknownId}/cancel` returns 404.
- `pageSize=0` returns 400; `pageSize=101` returns 400.
- The `types`, `statuses`, `sources` query parameters accept comma-separated values and filter correctly.
- Cancel and retry return the updated `OperationDetailDto` on success (200).
