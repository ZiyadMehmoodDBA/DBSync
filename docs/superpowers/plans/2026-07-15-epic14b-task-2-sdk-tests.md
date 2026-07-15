# Epic 14B — Task 2: MSOSync.SdkTests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create `MSOSync.SdkTests` project with a golden public-API surface test, `PluginBase` behaviour tests, and `PluginCapability` flags tests.

**Architecture:** Tests reference only `MSOSync.Sdk`. The golden API test uses reflection to enumerate all exported types and fails if the set changes — this forces any public API addition to be a deliberate decision. `PluginBase` tests verify default method behaviour and that `InitializeAsync` caches the context.

**Tech Stack:** C# 13 / .NET 9 / xUnit + FluentAssertions

## Global Constraints

- `MSOSync.SdkTests` references only `MSOSync.Sdk` (no `MSOSync.Plugin`)
- `TreatWarningsAsErrors=true`
- Package versions from `Directory.Packages.props`

## Files

**Create:**
- `tests/MSOSync.SdkTests/MSOSync.SdkTests.csproj`
- `tests/MSOSync.SdkTests/PublicApiTests.cs`
- `tests/MSOSync.SdkTests/PluginBaseTests.cs`
- `tests/MSOSync.SdkTests/PluginCapabilityTests.cs`

**Modify:**
- `MSOSync.sln` — add MSOSync.SdkTests project entry

## Interfaces

**Consumes:**
- `IPlugin`, `IPluginContext`, `IPluginConfiguration`, `IPluginServices`, `IPluginLogger`, `IPluginEnvironment` (Task 1)
- `PluginBase`, `PluginMetadata`, `PluginCapability`, `PluginPermission` (Task 1)

**Produces:** Test project (no types consumed by other tasks)

---

- [ ] **Step 1: Create `tests/MSOSync.SdkTests/MSOSync.SdkTests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

```powershell
dotnet sln MSOSync.sln add tests\MSOSync.SdkTests\MSOSync.SdkTests.csproj
```

Expected: `Project 'tests\MSOSync.SdkTests\MSOSync.SdkTests.csproj' added to the solution.`

- [ ] **Step 3: Create `tests/MSOSync.SdkTests/PublicApiTests.cs`**

```csharp
using FluentAssertions;
using MSOSync.Sdk.Abstractions;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PublicApiTests
{
    [Fact]
    public void MSOSync_Sdk_PublicApiSurface_MatchesSnapshot()
    {
        var assembly    = typeof(IPlugin).Assembly;
        var publicTypes = assembly
            .GetExportedTypes()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => t.FullName!)
            .ToList();

        var expected = new[]
        {
            "MSOSync.Sdk.Abstractions.IPlugin",
            "MSOSync.Sdk.Abstractions.IPluginConfiguration",
            "MSOSync.Sdk.Abstractions.IPluginContext",
            "MSOSync.Sdk.Abstractions.IPluginEnvironment",
            "MSOSync.Sdk.Abstractions.IPluginLogger",
            "MSOSync.Sdk.Abstractions.IPluginServices",
            "MSOSync.Sdk.Hosting.PluginBase",
            "MSOSync.Sdk.Metadata.PluginCapability",
            "MSOSync.Sdk.Metadata.PluginMetadata",
            "MSOSync.Sdk.Metadata.PluginPermission",
        };

        publicTypes.Should().BeEquivalentTo(expected,
            "the public API surface must not change without updating this snapshot");
    }
}
```

- [ ] **Step 4: Run the golden test to make sure it passes**

```powershell
dotnet test tests\MSOSync.SdkTests --filter "PublicApiTests" -v minimal
```

Expected: 1 test passes. If it fails with a mismatch, the list of types in the test needs updating.

- [ ] **Step 5: Create `tests/MSOSync.SdkTests/PluginBaseTests.cs`**

```csharp
using FluentAssertions;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;
using MSOSync.Sdk.Metadata;
using Moq;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PluginBaseTests
{
    private sealed class ConcretePlugin : PluginBase { }

    private static IPluginContext FakeContext()
    {
        var ctx = new Mock<IPluginContext>();
        ctx.Setup(c => c.Metadata).Returns(new PluginMetadata
        {
            PluginId    = "test",
            Name        = "Test",
            Version     = "1.0.0",
            SdkVersion  = "1.0",
            ApiVersion  = "1",
            Author      = "Test",
            Description = "desc"
        });
        return ctx.Object;
    }

    [Fact]
    public async Task InitializeAsync_DefaultImpl_SetsContext()
    {
        var plugin  = new ConcretePlugin();
        var context = FakeContext();

        await plugin.InitializeAsync(context, default);

        plugin.Invoking(p => p.Context).Should().NotThrow();
    }

    [Fact]
    public async Task StartAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.StartAsync(default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.StopAsync(default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_CachesContext_ContextPropertyAvailable()
    {
        var plugin  = new ConcretePlugin();
        var context = FakeContext();

        await plugin.InitializeAsync(context, default);

        // Access via reflection since Context is protected
        var prop = typeof(PluginBase).GetProperty("Context",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var value = prop!.GetValue(plugin);
        value.Should().BeSameAs(context);
    }
}
```

Note: `PluginBase.Context` is `protected`. The test accesses it via reflection. If `Context` needs to be testable, consider making it `internal` or `protected internal`. For now, reflection is acceptable in tests.

- [ ] **Step 6: Create `tests/MSOSync.SdkTests/PluginCapabilityTests.cs`**

```csharp
using FluentAssertions;
using MSOSync.Sdk.Metadata;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PluginCapabilityTests
{
    [Fact]
    public void PluginCapability_None_IsZero()
    {
        ((int)PluginCapability.None).Should().Be(0);
    }

    [Fact]
    public void PluginCapability_BitwiseCombination_Works()
    {
        var combined = PluginCapability.Collector | PluginCapability.Transport;
        combined.HasFlag(PluginCapability.Collector).Should().BeTrue();
        combined.HasFlag(PluginCapability.Transport).Should().BeTrue();
        combined.HasFlag(PluginCapability.Operation).Should().BeFalse();
    }

    [Fact]
    public void PluginCapability_AllValuesDistinct_NoPowerOfTwoCollisions()
    {
        var values = Enum.GetValues<PluginCapability>()
            .Where(v => v != PluginCapability.None)
            .ToList();

        foreach (var v1 in values)
        foreach (var v2 in values)
        {
            if (v1 == v2) continue;
            (v1 & v2).Should().Be(PluginCapability.None,
                $"{v1} and {v2} should not share bits");
        }
    }
}
```

- [ ] **Step 7: Run all SDK tests**

```powershell
dotnet test tests\MSOSync.SdkTests -v minimal
```

Expected: All tests pass. Fix any compilation errors (likely `Moq` not in `Directory.Packages.props` under SdkTests — it is already there since MSOSync.PluginTests uses it).

- [ ] **Step 8: Commit**

```powershell
git add tests\MSOSync.SdkTests\ MSOSync.sln
git commit -m "feat(14B-2): MSOSync.SdkTests — golden API surface test, PluginBase tests, PluginCapability tests"
```
