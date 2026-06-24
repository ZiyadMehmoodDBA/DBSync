# Task 2: ErrorSeverityClassifier

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Implement the `IErrorSeverityClassifier` interface and `ErrorSeverityClassifier` singleton that maps `ConflictType` strings to `ErrorSeverity` enum values. The reverse lookup (`GetConflictTypes`) is used by `BatchErrorQueryService` to translate severity filters into SQL `WHERE conflict_type IN (...)` clauses.

**Files:**
- Create: `src/MSOSync.Metadata/BatchErrors/IErrorSeverityClassifier.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/ErrorSeverityClassifier.cs`
- Create: `tests/MSOSync.MetadataTests/BatchErrors/ErrorSeverityClassifierTests.cs`

**Interfaces:**
- Consumes: `ErrorSeverity` enum (Task 1)
- Produces:
  - `IErrorSeverityClassifier.Classify(string? conflictType) → ErrorSeverity`
  - `IErrorSeverityClassifier.GetConflictTypes(ErrorSeverity severity) → IReadOnlyList<string>`

---

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.MetadataTests/BatchErrors/ErrorSeverityClassifierTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class ErrorSeverityClassifierTests
{
    private static IErrorSeverityClassifier Sut() => new ErrorSeverityClassifier();

    [Fact]
    public void Classify_Null_ReturnsCritical()
    {
        Sut().Classify(null).Should().Be(ErrorSeverity.Critical);
    }

    [Fact]
    public void Classify_Unknown_ReturnsCritical()
    {
        Sut().Classify("SomeUnknownType").Should().Be(ErrorSeverity.Critical);
    }

    [Fact]
    public void Classify_DuplicateKey_ReturnsInfo()
    {
        Sut().Classify("DuplicateKey").Should().Be(ErrorSeverity.Info);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Deadlock")]
    [InlineData("SequenceGap")]
    public void Classify_RetriableTypes_ReturnWarning(string conflictType)
    {
        Sut().Classify(conflictType).Should().Be(ErrorSeverity.Warning);
    }

    [Fact]
    public void Classify_MetadataMissing_ReturnsCritical()
    {
        Sut().Classify("MetadataMissing").Should().Be(ErrorSeverity.Critical);
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void GetConflictTypes_ReturnsNonEmpty(ErrorSeverity severity)
    {
        Sut().GetConflictTypes(severity).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void GetConflictTypes_ReturnsNoNulls(ErrorSeverity severity)
    {
        Sut().GetConflictTypes(severity).Should().NotContainNulls();
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void RoundTrip_AllTypesClassifyBackCorrectly(ErrorSeverity severity)
    {
        var sut   = Sut();
        var types = sut.GetConflictTypes(severity);
        foreach (var t in types)
            sut.Classify(t).Should().Be(severity, because: $"'{t}' should classify as {severity}");
    }

    [Fact]
    public void GetConflictTypes_SetsAreDisjoint()
    {
        var sut  = Sut();
        var info = sut.GetConflictTypes(ErrorSeverity.Info).ToHashSet();
        var warn = sut.GetConflictTypes(ErrorSeverity.Warning).ToHashSet();
        var crit = sut.GetConflictTypes(ErrorSeverity.Critical).ToHashSet();

        info.Intersect(warn).Should().BeEmpty();
        info.Intersect(crit).Should().BeEmpty();
        warn.Intersect(crit).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails (compile error — types don't exist yet)**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests\MSOSync.MetadataTests -c Debug
```

Expected: compile error — `IErrorSeverityClassifier` and `ErrorSeverityClassifier` not found.

- [ ] **Step 3: Create interface**

Create `src/MSOSync.Metadata/BatchErrors/IErrorSeverityClassifier.cs`:

```csharp
namespace MSOSync.Metadata.BatchErrors;

public interface IErrorSeverityClassifier
{
    ErrorSeverity Classify(string? conflictType);

    /// <summary>
    /// Returns non-null conflict type strings that map to the given severity.
    /// Used to translate severity filters into SQL WHERE conflict_type IN (...).
    /// </summary>
    IReadOnlyList<string> GetConflictTypes(ErrorSeverity severity);
}
```

- [ ] **Step 4: Implement ErrorSeverityClassifier**

Create `src/MSOSync.Metadata/BatchErrors/ErrorSeverityClassifier.cs`:

```csharp
using System.Collections.Frozen;

namespace MSOSync.Metadata.BatchErrors;

public sealed class ErrorSeverityClassifier : IErrorSeverityClassifier
{
    private static readonly FrozenDictionary<string, ErrorSeverity> Map =
        new Dictionary<string, ErrorSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["DuplicateKey"]     = ErrorSeverity.Info,
            ["Timeout"]          = ErrorSeverity.Warning,
            ["Deadlock"]         = ErrorSeverity.Warning,
            ["SequenceGap"]      = ErrorSeverity.Warning,
            ["MetadataMissing"]  = ErrorSeverity.Critical,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> InfoTypes    = GetTypes(ErrorSeverity.Info);
    private static readonly IReadOnlyList<string> WarningTypes = GetTypes(ErrorSeverity.Warning);
    private static readonly IReadOnlyList<string> CriticalTypes = GetTypes(ErrorSeverity.Critical);

    public ErrorSeverity Classify(string? conflictType)
    {
        if (conflictType is null) return ErrorSeverity.Critical;
        return Map.TryGetValue(conflictType, out var sev) ? sev : ErrorSeverity.Critical;
    }

    public IReadOnlyList<string> GetConflictTypes(ErrorSeverity severity) => severity switch
    {
        ErrorSeverity.Info     => InfoTypes,
        ErrorSeverity.Warning  => WarningTypes,
        ErrorSeverity.Critical => CriticalTypes,
        _                      => Array.Empty<string>()
    };

    private static IReadOnlyList<string> GetTypes(ErrorSeverity target) =>
        Map.Where(kvp => kvp.Value == target)
           .Select(kvp => kvp.Key)
           .ToArray();
}
```

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~ErrorSeverityClassifier" --logger "console;verbosity=normal"
```

Expected: all 10 tests PASS.

- [ ] **Step 6: Verify build**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/BatchErrors/IErrorSeverityClassifier.cs
git add src/MSOSync.Metadata/BatchErrors/ErrorSeverityClassifier.cs
git add tests/MSOSync.MetadataTests/BatchErrors/ErrorSeverityClassifierTests.cs
git commit -m "feat(9a): add IErrorSeverityClassifier + ErrorSeverityClassifier"
```
