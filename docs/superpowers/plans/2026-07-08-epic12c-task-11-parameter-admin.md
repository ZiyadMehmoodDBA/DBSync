# Epic 12C Task 11: SyncParameter Category Filter + PARAMETER_UPDATED Audit + ParametersController Extension

**Goal:** Extend the parameters API to support category-based filtering (for the Admin Center UI), capture old values before updates, emit `PARAMETER_UPDATED` audit entries with old-and-new-value diffs, and enrich `ParameterDto` with full metadata fields.

**Prerequisites:** `ParametersController`, `IParameterMetadataService`, `ParameterMetadataService`, and `ParameterChangedEvent` already exist. `IAuditService` (or equivalent) is available in the Metadata layer.

---

## Step 1: Locate existing files

- [ ] Confirm the following files exist, adjusting paths if the project structure differs:

| File | Expected path |
|---|---|
| ParametersController | `src/MSOSync.Api/Controllers/ParametersController.cs` |
| IParameterMetadataService | `src/MSOSync.Metadata/Services/IParameterMetadataService.cs` |
| ParameterMetadataService | `src/MSOSync.Metadata/Services/ParameterMetadataService.cs` |
| ParameterChangedEvent | `src/MSOSync.Metadata/Events/ParameterChangedEvent.cs` |
| ParameterDto | `src/MSOSync.Metadata/Dtos/ParameterDto.cs` (or inline in the service file) |

- [ ] Open each file and read its current state before making changes.

---

## Step 2: Update ParameterDto.cs

- [ ] Open `ParameterDto.cs` (wherever it lives — search for `record ParameterDto` if the path above is wrong)
- [ ] If the record already exists, replace it with the full field set. If it does not exist, create the file at `src/MSOSync.Metadata/Dtos/ParameterDto.cs`.
- [ ] The ParameterDto must contain exactly these fields (add any that are missing, do not remove any that already exist in the DB mapping):

```csharp
namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDto(
    string ParameterName,
    string? ParameterValue,
    string? Category,
    string? DisplayName,
    string? Description,
    int? DisplayOrder,
    string? ValueType,
    string? MinimumValue,
    string? MaximumValue,
    string? AllowedValues,
    bool IsSecret,
    bool IsDynamic,
    bool RequiresRestart);
```

**Important:** If the `sync_parameters` table does not have columns for every field above, add them with sensible defaults in the projection query rather than failing. For example:
- If `IsSecret` column does not exist: `IsSecret: false`
- If `Category` column does not exist: `Category: null`

---

## Step 3: Update IParameterMetadataService.cs

- [ ] Open `src/MSOSync.Metadata/Services/IParameterMetadataService.cs`
- [ ] Find the existing `GetAllAsync` method signature. It likely looks like:

```csharp
Task<IEnumerable<ParameterDto>> GetAllAsync(CancellationToken ct);
```

- [ ] Change it to include the optional category parameter:

```csharp
Task<IEnumerable<ParameterDto>> GetAllAsync(string? category, CancellationToken ct);
```

- [ ] Leave all other method signatures unchanged.

---

## Step 4: Update ParameterMetadataService.cs — GetAllAsync

- [ ] Open `src/MSOSync.Metadata/Services/ParameterMetadataService.cs`
- [ ] Find the `GetAllAsync` implementation. It currently looks something like:

```csharp
public async Task<IEnumerable<ParameterDto>> GetAllAsync(CancellationToken ct)
{
    return await db.Parameters
        .AsNoTracking()
        .Select(p => new ParameterDto(...))
        .ToListAsync(ct);
}
```

- [ ] Update the signature and add the category filter:

```csharp
public async Task<IEnumerable<ParameterDto>> GetAllAsync(string? category, CancellationToken ct)
{
    return await db.Parameters
        .AsNoTracking()
        .Where(p => category == null || p.Category == category)  // ADJUST: confirm property name is Category
        .OrderBy(p => p.DisplayOrder ?? 0)
        .ThenBy(p => p.ParameterName)
        .Select(p => new ParameterDto(
            p.ParameterName,
            p.ParameterValue,
            p.Category,
            p.DisplayName,
            p.Description,
            p.DisplayOrder,
            p.ValueType,
            p.MinimumValue,
            p.MaximumValue,
            p.AllowedValues,
            p.IsSecret,      // if column does not exist, use: false
            p.IsDynamic,     // if column does not exist, use: false
            p.RequiresRestart // if column does not exist, use: false
        ))
        .ToListAsync(ct);
}
```

**Adjust property names** to match the actual `sync_parameters` table columns. Properties that do not exist in the entity should be replaced with literal defaults (e.g., `false`, `null`, `0`).

---

## Step 5: Update ParameterMetadataService.cs — UpdateAsync (capture old value + emit audit)

- [ ] In the same file, find `UpdateAsync`. It currently looks something like:

```csharp
public async Task UpdateAsync(string name, string? newValue, string actor, CancellationToken ct)
{
    var param = await db.Parameters.FirstOrDefaultAsync(p => p.ParameterName == name, ct);
    if (param is null) throw new NotFoundException(...);
    param.ParameterValue = newValue;
    await db.SaveChangesAsync(ct);
    await publisher.Publish(new ParameterChangedEvent(name, newValue), ct);
}
```

- [ ] Replace with the following (adjust property names as needed):

```csharp
public async Task UpdateAsync(string name, string? newValue, string actor, CancellationToken ct)
{
    // Step 1: Read the current value BEFORE updating (using AsNoTracking to avoid cache issues)
    var current = await db.Parameters
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.ParameterName == name, ct);

    if (current is null)
        throw new KeyNotFoundException($"Parameter '{name}' not found.");

    var oldValue = current.ParameterValue;  // ADJUST: confirm property name

    // Step 2: Apply the update
    var tracked = await db.Parameters
        .FirstOrDefaultAsync(p => p.ParameterName == name, ct);
    tracked!.ParameterValue = newValue;     // ADJUST: confirm property name
    await db.SaveChangesAsync(ct);

    // Step 3: Write PARAMETER_UPDATED audit entry
    await auditSvc.WriteAsync(
        "PARAMETER_UPDATED",
        $"{name}: '{oldValue}' → '{newValue}'",
        actor,
        ct);

    // Step 4: Publish event with old value included
    await publisher.Publish(new ParameterChangedEvent(name, oldValue, newValue), ct);
}
```

**Note:** If `auditSvc` is not currently injected into `ParameterMetadataService`, add it to the constructor:

```csharp
public sealed class ParameterMetadataService(
    AppDbContext db,
    IPublisher publisher,
    IAuditService auditSvc)    // add this
    : IParameterMetadataService
```

Replace `IAuditService` with the actual audit service interface name used in this project (search for `WriteAsync` in the audit layer to find it).

---

## Step 6: Update ParameterChangedEvent.cs

- [ ] Open `src/MSOSync.Metadata/Events/ParameterChangedEvent.cs`
- [ ] The current record probably looks like:

```csharp
public sealed record ParameterChangedEvent(
    string ParameterName, string? NewValue) : INotification;
```

- [ ] Add the `OldValue` field so it becomes:

```csharp
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record ParameterChangedEvent(
    string ParameterName,
    string? OldValue,
    string? NewValue) : INotification;
```

**After this change:** search for all existing usages of `new ParameterChangedEvent(` in the codebase and add `null` as the second argument (OldValue) if any callsite only passes 2 arguments. Fix all compiler errors before proceeding.

- [ ] Run a search for `new ParameterChangedEvent` across the solution:
  - If found in test files or other service files with the 2-arg constructor, update each to pass `oldValue` or `null` explicitly

---

## Step 7: Update ParametersController.cs

- [ ] Open `src/MSOSync.Api/Controllers/ParametersController.cs`
- [ ] Find the `GetAllAsync` action method. It currently looks like:

```csharp
[HttpGet]
public async Task<IActionResult> GetAllAsync(CancellationToken ct = default)
    => Ok(await paramSvc.GetAllAsync(ct));
```

- [ ] Replace it with:

```csharp
[HttpGet]
[ProducesResponseType<IEnumerable<ParameterDto>>(200)]
public async Task<IActionResult> GetAllAsync(
    [FromQuery] string? category = null,
    CancellationToken ct = default)
    => Ok(await paramSvc.GetAllAsync(category, ct));
```

- [ ] Add using directive at the top if not already present:

```csharp
using MSOSync.Metadata.Dtos;
```

---

## Step 8: Fix any other callers of GetAllAsync that broke due to the signature change

- [ ] Search the solution for all calls to `.GetAllAsync(` that do not pass a category argument
- [ ] Update each call to pass `null` as the first argument:

```csharp
// Before:
var all = await paramSvc.GetAllAsync(ct);
// After:
var all = await paramSvc.GetAllAsync(null, ct);
```

---

## Step 9: Build the solution

- [ ] Run `dotnet build MSOSync.sln`
- [ ] Expect 0 errors. Common issues:
  - `ParameterChangedEvent` callsites with wrong number of arguments — fix each one
  - `GetAllAsync` callsites with old signature — fix each one
  - Missing `IAuditService` injection — confirm the actual audit interface name

---

## Step 10: Write unit tests

- [ ] Open (or create) `tests/MSOSync.AppTests/Parameters/ParameterMetadataServiceTests.cs`
- [ ] Paste the following tests. Adjust entity and property names as needed.

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Infrastructure.Persistence;      // ADJUST
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using NSubstitute;
using Xunit;

namespace MSOSync.AppTests.Parameters;

public sealed class ParameterMetadataServiceTests : IDisposable
{
    private readonly AppDbContext _db;          // ADJUST
    private readonly IPublisher _publisher;
    private readonly object _auditSvc;          // ADJUST: replace with actual IAuditService type

    public ParameterMetadataServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()  // ADJUST
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);        // ADJUST
        _publisher = Substitute.For<IPublisher>();
        _auditSvc = Substitute.For<object>();   // ADJUST: Substitute.For<IAuditService>()
    }

    private ParameterMetadataService CreateService()
    {
        // ADJUST: pass correct constructor args
        // return new ParameterMetadataService(_db, _publisher, (IAuditService)_auditSvc);
        throw new NotImplementedException("Replace with actual constructor call once audit service interface is known.");
    }

    // Helper: seed a parameter entity
    // ADJUST: replace with actual entity type
    private async Task SeedParameterAsync(string name, string? value, string? category)
    {
        // ADJUST: replace with actual entity
        // _db.Parameters.Add(new SyncParameter
        // {
        //     ParameterName = name,
        //     ParameterValue = value,
        //     Category = category
        // });
        // await _db.SaveChangesAsync();
        throw new NotImplementedException("Replace with actual entity seeding.");
    }

    // Test 1: GetAllAsync with no category returns all parameters
    [Fact]
    public async Task GetAllAsync_NoCategoryFilter_ReturnsAll()
    {
        await SeedParameterAsync("Param1", "Value1", "FeatureFlag");
        await SeedParameterAsync("Param2", "Value2", "Timeout");
        await SeedParameterAsync("Param3", "Value3", null);

        var svc = CreateService();
        var all = await svc.GetAllAsync(null, CancellationToken.None);

        Assert.Equal(3, all.Count());
    }

    // Test 2: GetAllAsync with category="FeatureFlag" returns only that category
    [Fact]
    public async Task GetAllAsync_WithCategoryFilter_ReturnsOnlyMatchingCategory()
    {
        await SeedParameterAsync("Flag1", "true", "FeatureFlag");
        await SeedParameterAsync("Flag2", "false", "FeatureFlag");
        await SeedParameterAsync("Timeout1", "30", "Timeout");

        var svc = CreateService();
        var flags = await svc.GetAllAsync("FeatureFlag", CancellationToken.None);

        Assert.Equal(2, flags.Count());
        Assert.All(flags, p => Assert.Equal("FeatureFlag", p.Category));
    }

    // Test 3: UpdateAsync emits PARAMETER_UPDATED audit with old + new value
    [Fact]
    public async Task UpdateAsync_EmitsAuditWithOldAndNewValue()
    {
        await SeedParameterAsync("MyParam", "oldValue", "General");

        var svc = CreateService();
        await svc.UpdateAsync("MyParam", "newValue", "testuser", CancellationToken.None);

        // ADJUST: replace IAuditService with actual interface and WriteAsync with actual method
        // await ((IAuditService)_auditSvc).Received(1).WriteAsync(
        //     "PARAMETER_UPDATED",
        //     Arg.Is<string>(s => s.Contains("oldValue") && s.Contains("newValue")),
        //     "testuser",
        //     Arg.Any<CancellationToken>());
        Assert.True(true, "Implement once IAuditService interface name is confirmed.");
    }

    // Test 4: ParameterChangedEvent published with OldValue
    [Fact]
    public async Task UpdateAsync_PublishesParameterChangedEventWithOldValue()
    {
        await SeedParameterAsync("MyParam", "before", "General");

        var svc = CreateService();
        await svc.UpdateAsync("MyParam", "after", "testuser", CancellationToken.None);

        await _publisher.Received(1).Publish(
            Arg.Is<ParameterChangedEvent>(e =>
                e.ParameterName == "MyParam" &&
                e.OldValue == "before" &&
                e.NewValue == "after"),
            Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
```

**Note:** Tests 1, 2, and 4 require replacing the `NotImplementedException` stubs with actual entity and service construction. Test 4 requires only the publisher mock to be wired correctly and will pass as soon as `CreateService()` is implemented. Implement stubs after confirming entity shapes.

- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` after implementing stubs — expect all 4 tests to pass.

---

## Acceptance criteria

- `GET /api/v1/parameters` with no query param returns all parameters
- `GET /api/v1/parameters?category=FeatureFlag` returns only parameters with `Category == "FeatureFlag"`
- `PUT /api/v1/parameters/{name}` writes a `PARAMETER_UPDATED` audit entry with format `"name: 'old' → 'new'"`
- `ParameterChangedEvent.OldValue` is set to the pre-update value
- `ParameterDto` includes all 13 fields from the spec
- `dotnet build MSOSync.sln` produces 0 errors
- All 4 unit tests pass once stubs are implemented
