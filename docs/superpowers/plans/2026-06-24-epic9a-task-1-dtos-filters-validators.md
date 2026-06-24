# Task 1: PagedResult Refactor + DTOs + Filter Classes + Filter Validators

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Move `PagedResult<T>` to the shared `MSOSync.Metadata.Common` namespace, create all DTO types and filter classes, add FluentValidation to the Metadata project, and implement filter validators with unit tests.

**Files:**
- Create: `src/MSOSync.Metadata/Common/PagedResult.cs`
- Create: `src/MSOSync.Metadata/Events/EventSummaryDto.cs`
- Create: `src/MSOSync.Metadata/Events/EventDetailDto.cs`
- Create: `src/MSOSync.Metadata/Events/EventFilter.cs`
- Create: `src/MSOSync.Metadata/Events/EventFilterValidator.cs`
- Create: `src/MSOSync.Metadata/IncomingBatches/IncomingBatchSummaryDto.cs`
- Create: `src/MSOSync.Metadata/IncomingBatches/IncomingBatchDetailDto.cs`
- Create: `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs`
- Create: `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryDto.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorDetailDto.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryCountDto.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorFilter.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorFilterValidator.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/ErrorSeverity.cs`
- Delete: `src/MSOSync.Metadata/Users/PagedResult.cs`
- Modify: `src/MSOSync.Metadata/Users/UsersManagementService.cs`
- Modify: `src/MSOSync.Metadata/Users/IUsersManagementService.cs`
- Modify: `src/MSOSync.Metadata/MSOSync.Metadata.csproj`
- Modify: `tests/MSOSync.IntegrationTests/Users/UsersTests.cs`
- Create: `tests/MSOSync.MetadataTests/Events/EventFilterValidatorTests.cs`
- Create: `tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchFilterValidatorTests.cs`
- Create: `tests/MSOSync.MetadataTests/BatchErrors/BatchErrorFilterValidatorTests.cs`

**Interfaces:**
- Produces: `PagedResult<T>` in `MSOSync.Metadata.Common` — used by Tasks 3–5 and integration tests
- Produces: all DTO types — used by Tasks 3–6
- Produces: all filter classes — used by Tasks 3–6
- Produces: `EventFilterValidator`, `IncomingBatchFilterValidator`, `BatchErrorFilterValidator` — used by Task 6 controllers via DI
- Produces: `ErrorSeverity` enum — used by Task 2 classifier and Tasks 5–6

---

- [ ] **Step 1: Add FluentValidation to MSOSync.Metadata.csproj**

Open `src/MSOSync.Metadata/MSOSync.Metadata.csproj`. Current content:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>NodeMetadata, TriggerMetadata, RouterMetadata, ChannelMetadata, ParameterMetadata</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Security\MSOSync.Security.csproj" />
  </ItemGroup>
</Project>
```

Add `FluentValidation` to the first ItemGroup:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>NodeMetadata, TriggerMetadata, RouterMetadata, ChannelMetadata, ParameterMetadata</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Security\MSOSync.Security.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing validator tests**

Create `tests/MSOSync.MetadataTests/Events/EventFilterValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.Events;
using Xunit;

namespace MSOSync.MetadataTests.Events;

public sealed class EventFilterValidatorTests
{
    private static EventFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        var result = Sut().Validate(new EventFilter { Page = 0 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        var result = Sut().Validate(new EventFilter { PageSize = 101 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public void PageSize_Zero_Fails()
    {
        var result = Sut().Validate(new EventFilter { PageSize = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        var result = Sut().Validate(new EventFilter
        {
            From = now.AddHours(1),
            To   = now
        });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To");
    }

    [Fact]
    public void Valid_Defaults_Pass()
    {
        var result = Sut().Validate(new EventFilter());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_WithAll_Pass()
    {
        var now = DateTime.UtcNow;
        var result = Sut().Validate(new EventFilter
        {
            SourceNodeId = "node-1",
            TriggerId    = "trig-1",
            EventType    = 'I',
            IsProcessed  = false,
            From         = now.AddDays(-1),
            To           = now,
            Page         = 2,
            PageSize     = 100
        });
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run test to verify it fails (compile error — types don't exist yet)**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests\MSOSync.MetadataTests -c Debug
```

Expected: build error — `EventFilter` and `EventFilterValidator` not found.

- [ ] **Step 4: Create PagedResult<T> in Common namespace**

Create `src/MSOSync.Metadata/Common/PagedResult.cs`:

```csharp
namespace MSOSync.Metadata.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Page,
    int              PageSize,
    int              TotalCount);
```

- [ ] **Step 5: Update UsersManagementService and IUsersManagementService**

In `src/MSOSync.Metadata/Users/UsersManagementService.cs`, replace `using MSOSync.Metadata.Users;` with:
```csharp
using MSOSync.Metadata.Common;
```
And remove the import if it was implicit through the namespace.

The actual change: at the top of `UsersManagementService.cs`, add:
```csharp
using MSOSync.Metadata.Common;
```

In `src/MSOSync.Metadata/Users/IUsersManagementService.cs`, add:
```csharp
using MSOSync.Metadata.Common;
```

- [ ] **Step 6: Delete old PagedResult.cs from Users/**

Delete the file `src/MSOSync.Metadata/Users/PagedResult.cs`. It is replaced by `Common/PagedResult.cs`.

The file currently contains:
```csharp
namespace MSOSync.Metadata.Users;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Page,
    int              PageSize,
    int              TotalCount);
```

Delete it (the new one in `Common/` replaces it).

- [ ] **Step 7: Fix UsersTests namespace reference**

Open `tests/MSOSync.IntegrationTests/Users/UsersTests.cs`. Search for any reference to `MSOSync.Metadata.Users.PagedResult` or `PagedResult<` deserialization. If there is a `using MSOSync.Metadata.Users;` that imports `PagedResult<T>`, add `using MSOSync.Metadata.Common;` instead (or in addition to).

- [ ] **Step 8: Create ErrorSeverity enum**

Create `src/MSOSync.Metadata/BatchErrors/ErrorSeverity.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public enum ErrorSeverity { Info, Warning, Critical }
```

- [ ] **Step 9: Create Events DTOs and filter**

Create `src/MSOSync.Metadata/Events/EventSummaryDto.cs`:

```csharp
namespace MSOSync.Metadata.Events;

public sealed record EventSummaryDto(
    long     EventId,
    string   TriggerId,
    string   SourceNodeId,
    string   ChannelId,
    char     EventType,
    string   TableName,
    long?    BatchId,
    DateTime CreateTime,
    bool     IsProcessed);
```

Create `src/MSOSync.Metadata/Events/EventDetailDto.cs`:

```csharp
namespace MSOSync.Metadata.Events;

public sealed record EventDetailDto(
    long     EventId,
    string   TriggerId,
    string   SourceNodeId,
    string   ChannelId,
    char     EventType,
    string   TableName,
    string?  PkData,
    string?  RowData,
    long?    TransactionId,
    long?    BatchId,
    DateTime CreateTime,
    bool     IsProcessed);
```

Create `src/MSOSync.Metadata/Events/EventFilter.cs`:

```csharp
namespace MSOSync.Metadata.Events;

public sealed class EventFilter
{
    public string?   SourceNodeId { get; set; }
    public string?   TriggerId    { get; set; }
    public string?   ChannelId    { get; set; }
    public char?     EventType    { get; set; }
    public bool?     IsProcessed  { get; set; }
    public DateTime? From         { get; set; }
    public DateTime? To           { get; set; }
    public int       Page         { get; set; } = 1;
    public int       PageSize     { get; set; } = 50;
}
```

Create `src/MSOSync.Metadata/Events/EventFilterValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Metadata.Events;

public sealed class EventFilterValidator : AbstractValidator<EventFilter>
{
    public EventFilterValidator()
    {
        RuleFor(f => f.Page).GreaterThanOrEqualTo(1);
        RuleFor(f => f.PageSize).InclusiveBetween(1, 100);
        RuleFor(f => f.To)
            .GreaterThanOrEqualTo(f => f.From!.Value)
            .When(f => f.From.HasValue && f.To.HasValue)
            .WithMessage("'To' must be greater than or equal to 'From'.");
    }
}
```

- [ ] **Step 10: Create IncomingBatches DTOs and filter**

Create `src/MSOSync.Metadata/IncomingBatches/IncomingBatchSummaryDto.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed record IncomingBatchSummaryDto(
    long                 BatchId,
    string               SourceNodeId,
    string               ChannelId,
    IncomingBatchStatus  Status,
    int?                 RowCount,
    long                 BatchSequence,
    DateTime             ReceivedTime,
    long?                ApplyTimeMs);
```

Create `src/MSOSync.Metadata/IncomingBatches/IncomingBatchDetailDto.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed record IncomingBatchDetailDto(
    long                 BatchId,
    string               SourceNodeId,
    string               ChannelId,
    IncomingBatchStatus  Status,
    int?                 RowCount,
    long                 BatchSequence,
    DateTime             ReceivedTime,
    DateTime?            LoadTime,
    DateTime?            ExtractTime,
    DateTime?            AppliedTime,
    long?                ApplyTimeMs);
```

Create `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchFilter
{
    public string?              SourceNodeId { get; set; }
    public string?              ChannelId    { get; set; }
    public IncomingBatchStatus? Status       { get; set; }
    public DateTime?            From         { get; set; }
    public DateTime?            To           { get; set; }
    public int                  Page         { get; set; } = 1;
    public int                  PageSize     { get; set; } = 50;
}
```

Create `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchFilterValidator : AbstractValidator<IncomingBatchFilter>
{
    public IncomingBatchFilterValidator()
    {
        RuleFor(f => f.Page).GreaterThanOrEqualTo(1);
        RuleFor(f => f.PageSize).InclusiveBetween(1, 100);
        RuleFor(f => f.To)
            .GreaterThanOrEqualTo(f => f.From!.Value)
            .When(f => f.From.HasValue && f.To.HasValue)
            .WithMessage("'To' must be greater than or equal to 'From'.");
    }
}
```

- [ ] **Step 11: Create BatchErrors DTOs and filter**

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryDto.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public sealed record BatchErrorSummaryDto(
    long     ErrorId,
    long     BatchId,
    long?    EventId,
    string?  ConflictType,
    string   Severity,
    string?  ErrorMessage,
    DateTime CreateTime,
    int      RetryCount);
```

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorDetailDto.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public sealed record BatchErrorDetailDto(
    long      ErrorId,
    long      BatchId,
    long?     EventId,
    string?   ConflictType,
    string    Severity,
    string?   ErrorMessage,
    DateTime  CreateTime,
    int       RetryCount,
    DateTime? LastRetryTime);
```

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryCountDto.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public sealed record BatchErrorSummaryCountDto(int Info, int Warning, int Critical, int Total);
```

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorFilter.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public sealed class BatchErrorFilter
{
    public long?          BatchId      { get; set; }
    public string?        ConflictType { get; set; }
    public ErrorSeverity? Severity     { get; set; }
    public DateTime?      From         { get; set; }
    public DateTime?      To           { get; set; }
    public int            Page         { get; set; } = 1;
    public int            PageSize     { get; set; } = 50;
}
```

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorFilterValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Metadata.BatchErrors;

public sealed class BatchErrorFilterValidator : AbstractValidator<BatchErrorFilter>
{
    public BatchErrorFilterValidator()
    {
        RuleFor(f => f.Page).GreaterThanOrEqualTo(1);
        RuleFor(f => f.PageSize).InclusiveBetween(1, 100);
        RuleFor(f => f.To)
            .GreaterThanOrEqualTo(f => f.From!.Value)
            .When(f => f.From.HasValue && f.To.HasValue)
            .WithMessage("'To' must be greater than or equal to 'From'.");
    }
}
```

- [ ] **Step 12: Create IncomingBatch and BatchError validator test files**

Create `tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchFilterValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.MetadataTests.IncomingBatches;

public sealed class IncomingBatchFilterValidatorTests
{
    private static IncomingBatchFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        Sut().Validate(new IncomingBatchFilter { Page = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        Sut().Validate(new IncomingBatchFilter { PageSize = 101 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        Sut().Validate(new IncomingBatchFilter { From = now.AddHours(1), To = now })
             .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_WithStatus_Passes()
    {
        Sut().Validate(new IncomingBatchFilter { Status = IncomingBatchStatus.Error })
             .IsValid.Should().BeTrue();
    }
}
```

Create `tests/MSOSync.MetadataTests/BatchErrors/BatchErrorFilterValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class BatchErrorFilterValidatorTests
{
    private static BatchErrorFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        Sut().Validate(new BatchErrorFilter { Page = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        Sut().Validate(new BatchErrorFilter { PageSize = 101 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        Sut().Validate(new BatchErrorFilter { From = now.AddHours(1), To = now })
             .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_WithSeverity_Passes()
    {
        Sut().Validate(new BatchErrorFilter { Severity = ErrorSeverity.Warning })
             .IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 13: Run all validator tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests\MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~FilterValidator" --logger "console;verbosity=normal"
```

Expected: all 15 validator tests PASS (5 EventFilter + 4 IncomingBatch + 4 BatchError + 2 others).

- [ ] **Step 14: Verify full solution builds**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 15: Commit**

```powershell
git add src/MSOSync.Metadata/Common/PagedResult.cs
git add src/MSOSync.Metadata/Events/EventSummaryDto.cs
git add src/MSOSync.Metadata/Events/EventDetailDto.cs
git add src/MSOSync.Metadata/Events/EventFilter.cs
git add src/MSOSync.Metadata/Events/EventFilterValidator.cs
git add src/MSOSync.Metadata/IncomingBatches/IncomingBatchSummaryDto.cs
git add src/MSOSync.Metadata/IncomingBatches/IncomingBatchDetailDto.cs
git add src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs
git add src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs
git add src/MSOSync.Metadata/BatchErrors/ErrorSeverity.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryDto.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorDetailDto.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorSummaryCountDto.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorFilter.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorFilterValidator.cs
git add src/MSOSync.Metadata/MSOSync.Metadata.csproj
git add src/MSOSync.Metadata/Users/UsersManagementService.cs
git add src/MSOSync.Metadata/Users/IUsersManagementService.cs
git add tests/MSOSync.MetadataTests/Events/EventFilterValidatorTests.cs
git add tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchFilterValidatorTests.cs
git add tests/MSOSync.MetadataTests/BatchErrors/BatchErrorFilterValidatorTests.cs
git rm src/MSOSync.Metadata/Users/PagedResult.cs
git add tests/MSOSync.IntegrationTests/Users/UsersTests.cs
git commit -m "feat(9a): add DTOs, filter classes, validators; move PagedResult to Common"
```
