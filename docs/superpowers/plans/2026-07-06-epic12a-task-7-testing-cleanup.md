# Epic 12A Task 7: Testing + Cleanup

> **For agentic workers:** This is Task 7 of 7 — the final task. All prior tasks must be complete and passing before starting this task.

**Goal:** Add authorization boundary tests, the concurrent-approval concurrency test, verify the full build is zero-warning, and confirm all NodeManagement tests are green.

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings required
- xUnit 2.9.3, FluentAssertions 6.12.2
- All tests use the shared `NodeManagementFixture` from Task 2 (`[Collection("NodeManagement")]`)
- Concurrency test: two parallel `HttpClient` tasks both attempt to approve the same registration; exactly one succeeds (204), exactly one fails (409 Conflict)
- Authorization matrix to cover: VIEW_TOPOLOGY cannot approve/reject/provision; APPROVE_NODES cannot provision; unauthenticated gets 401 on all authenticated endpoints

## Files

**Create:**
- `tests/MSOSync.IntegrationTests/NodeManagement/AuthorizationTests.cs`
- `tests/MSOSync.IntegrationTests/NodeManagement/ConcurrencyTests.cs`

**No other files to create or modify.** If the build has any warnings, fix them in the relevant source files.

---

## Steps

- [ ] **Step 1: Write AuthorizationTests**

```csharp
// tests/MSOSync.IntegrationTests/NodeManagement/AuthorizationTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class AuthorizationTests(NodeManagementFixture fixture)
{
    // ── Unauthenticated → 401 ────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrations_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .GetAsync("api/v1/node-management/registrations");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOverview_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provision_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/provision", new
            {
                nodeName   = "x",
                externalId = "x",
                nodeType   = "source",
                dbServer   = "s",
                dbName     = "d",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── VIEWER cannot approve/reject/provision → 403 ─────────────────────────

    [Fact]
    public async Task Approve_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/1/approve", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/1/reject", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkApprove_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-approve",
            new { ids = new[] { 1L } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkReject_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-reject",
            new { ids = new[] { 1L } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provision_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/provision", new
            {
                nodeName   = "blocked",
                externalId = "blocked-ext",
                nodeType   = "source",
                dbServer   = "sql",
                dbName     = "db",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── APPROVER (OPERATOR) cannot provision → 403 ────────────────────────────

    [Fact]
    public async Task Provision_ApproverRole_Returns403()
    {
        var approver = await fixture.ApproverClientAsync();

        var resp = await approver.PostAsJsonAsync(
            "api/v1/node-management/provision", new
            {
                nodeName   = "blocked-approver",
                externalId = "blocked-approver-ext",
                nodeType   = "source",
                dbServer   = "sql",
                dbName     = "db",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── APPROVER can read → 200 ───────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrations_ApproverRole_Returns200()
    {
        var approver = await fixture.ApproverClientAsync();

        var resp = await approver.GetAsync("api/v1/node-management/registrations");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── VIEWER can read → 200 ─────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_ViewerRole_Returns200()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /registrations is anonymous → 202 ────────────────────────────────

    [Fact]
    public async Task InboundRegistration_Anonymous_Returns202()
    {
        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/registrations", new
            {
                externalId = "anon-auth-test-node",
                nodeName   = "anon-node",
                nodeType   = "source",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
```

- [ ] **Step 2: Write ConcurrencyTests**

```csharp
// tests/MSOSync.IntegrationTests/NodeManagement/ConcurrencyTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class ConcurrencyTests(NodeManagementFixture fixture)
{
    [Fact]
    public async Task ConcurrentApprove_SameRegistration_OneSucceedsOneFails()
    {
        // Register a fresh node to avoid interference with other tests
        var anon = fixture.AnonymousClient();
        var regResp = await anon.PostAsJsonAsync(
            "api/v1/node-management/registrations",
            new
            {
                externalId = "concurrency-test-node",
                nodeName   = "concurrency-node",
                nodeType   = "source",
            });
        regResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = regBody.GetProperty("registrationId").GetInt64();

        // Two approvers race to approve the same registration
        var client1 = await fixture.ApproverClientAsync();
        var client2 = await fixture.ApproverClientAsync();

        var task1 = client1.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve", new { notes = "approver1" });
        var task2 = client2.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve", new { notes = "approver2" });

        var results = await Task.WhenAll(task1, task2);

        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(x => x).ToList();

        // Exactly one 204, exactly one 409
        statuses.Should().BeEquivalentTo(new[] { 204, 409 });
    }
}
```

- [ ] **Step 3: Run authorization tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement.AuthorizationTests" -c Debug
```

Expected: All authorization tests GREEN.

- [ ] **Step 4: Run concurrency test**

```pwsh
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement.ConcurrencyTests" -c Debug
```

Expected: GREEN. If the concurrency test is flaky (race too fast/slow), add a brief `Task.Delay` between starting tasks and confirming behavior. The EF Core rowversion concurrency check ensures one throws `DbUpdateConcurrencyException` → 409.

- [ ] **Step 5: Run all NodeManagement tests**

```pwsh
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement" -c Debug
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~NodeManagement" -c Debug
```

Expected: All green.

- [ ] **Step 6: Full build — zero warnings**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: Build succeeded, 0 warnings. Fix any warnings that appear (unused usings, nullable reference warnings, etc.).

- [ ] **Step 7: Full test suite — no regressions**

```pwsh
dotnet test MSOSync.sln -c Debug --filter "FullyQualifiedName~NodeManagement" --no-build
```

Expected: All NodeManagement tests green.

Run a quick sanity check that existing tests haven't regressed:

```pwsh
dotnet test tests/MSOSync.IntegrationTests -c Debug --no-build
```

Expected: All integration tests green (Dashboard, Audit, OperationalRead, etc. unchanged).

- [ ] **Step 8: Frontend final build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: Zero TypeScript errors, zero warnings. Fix any issues before committing.

- [ ] **Step 9: Commit**

```pwsh
git add `
  tests/MSOSync.IntegrationTests/NodeManagement/AuthorizationTests.cs `
  tests/MSOSync.IntegrationTests/NodeManagement/ConcurrencyTests.cs
git commit -m "feat(12A): authorization + concurrency integration tests, build clean"
```

---

## Verification (Epic 12A Complete)

After this commit, run the full verification from the master plan:

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~NodeManagement" -c Debug
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement" -c Debug
```

Expected: Build clean (zero warnings), all NodeManagement unit tests green, all NodeManagement integration tests green.
