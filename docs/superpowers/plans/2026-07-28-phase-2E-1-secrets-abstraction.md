# Phase 2E.1 — Secrets Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `MSOSync.Secrets` project with `ISecretsService`, `EnvironmentSecretsService`, and `CompositeSecretsService`; migrate existing hardcoded secret reads (JWT signing key, HMAC cursor key, node bootstrap token) to use the abstraction.

**Architecture:** New class library `MSOSync.Secrets` added to solution. `ISecretsService` is read-only. `EnvironmentSecretsService` reads env vars, falls back to `IConfiguration` in Development. `CompositeSecretsService` chains providers. `AddSecretsService` extension replaces the direct env-var reads in `SecurityServiceExtensions` and related startup code.

**Tech Stack:** C# 13 / .NET 9 / Microsoft.Extensions.DependencyInjection / Microsoft.Extensions.Configuration / Microsoft.Extensions.Hosting

## Global Constraints

- C# 13 / .NET 9, `sealed internal` for implementations, `public` for interfaces and extension methods
- `IOptions<SecretsOptions>` with `ValidateOnStart()` — `Provider` must be `"Environment"` or `"AzureKeyVault"`
- `EnvironmentSecretsService`: env var key mapping: replace `:` with `__` (e.g., `Jwt:SigningKey` → `JWT__SIGNINGKEY`; also try `MSOSYNC_JWT_SECRET` for backward compat)
- `CompositeSecretsService` returns first non-null from provider chain; providers injected as `IEnumerable<ISecretsService>`
- No breaking changes to `SecurityServiceExtensions` behaviour — existing `MSOSYNC_JWT_SECRET` env var continues to work
- `git add` by file name — never `git add -A`

---

### Task 1: ISecretsService interface + EnvironmentSecretsService

**Files:**
- Create: `src/MSOSync.Secrets/ISecretsService.cs`
- Create: `src/MSOSync.Secrets/EnvironmentSecretsService.cs`
- Create: `src/MSOSync.Secrets/MSOSync.Secrets.csproj`
- Create: `tests/MSOSync.SecretsTests/MSOSync.SecretsTests.csproj`
- Create: `tests/MSOSync.SecretsTests/EnvironmentSecretsServiceTests.cs`

**Interfaces:**
- Produces: `ISecretsService` with `GetSecretAsync(string key, CancellationToken ct)`, `GetSecretBytesAsync(string key, CancellationToken ct)`, `ExistsAsync(string key, CancellationToken ct)`

- [ ] **Step 1: Create MSOSync.Secrets project**

```xml
<!-- src/MSOSync.Secrets/MSOSync.Secrets.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
  </ItemGroup>
</Project>
```

Add to solution: `dotnet sln D:\MSOSync\MSOSync.sln add src/MSOSync.Secrets/MSOSync.Secrets.csproj`

- [ ] **Step 2: Write ISecretsService**

```csharp
// src/MSOSync.Secrets/ISecretsService.cs
namespace MSOSync.Secrets;

public interface ISecretsService
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create test project**

```xml
<!-- tests/MSOSync.SecretsTests/MSOSync.SecretsTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Secrets\MSOSync.Secrets.csproj" />
  </ItemGroup>
</Project>
```

Add to solution: `dotnet sln D:\MSOSync\MSOSync.sln add tests/MSOSync.SecretsTests/MSOSync.SecretsTests.csproj`

- [ ] **Step 4: Write failing tests for EnvironmentSecretsService**

```csharp
// tests/MSOSync.SecretsTests/EnvironmentSecretsServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class EnvironmentSecretsServiceTests : IDisposable
{
    private const string TestEnvVar = "MSOSYNC_TEST_SECRETS_KEY__VALUE";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, null);
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_KEY__VALUE", null);
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_SECRETS_KEY__VALUE", "test-secret");
        var svc = Build();

        var result = await svc.GetSecretAsync("MSOSYNC_TEST_SECRETS_KEY:VALUE");

        result.Should().Be("test-secret");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenEnvVarNotSet()
    {
        var svc = Build();

        var result = await svc.GetSecretAsync("NONEXISTENT_KEY");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_FallsBackToConfiguration_InDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SomeKey"] = "from-config" })
            .Build();
        var svc = new EnvironmentSecretsService(config, isProduction: false);

        var result = await svc.GetSecretAsync("SomeKey");

        result.Should().Be("from-config");
    }

    [Fact]
    public async Task GetSecretAsync_DoesNotFallBackToConfig_InProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SomeKey"] = "from-config" })
            .Build();
        var svc = new EnvironmentSecretsService(config, isProduction: true);

        var result = await svc.GetSecretAsync("SomeKey");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_KEY__VALUE", "anything");
        var svc = Build();

        var exists = await svc.ExistsAsync("MSOSYNC_TEST_KEY:VALUE");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetSecretBytesAsync_ReturnsUtf8Bytes_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_SECRETS_KEY__VALUE", "bytes-value");
        var svc = Build();

        var bytes = await svc.GetSecretBytesAsync("MSOSYNC_TEST_SECRETS_KEY:VALUE");

        bytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes("bytes-value"));
    }

    private static EnvironmentSecretsService Build() =>
        new(new ConfigurationBuilder().Build(), isProduction: true);
}
```

- [ ] **Step 5: Run tests — expect compile failure**

```
cd D:\MSOSync
dotnet test tests/MSOSync.SecretsTests --no-build 2>&1 | head -5
```
Expected: build error — `EnvironmentSecretsService` not yet defined.

- [ ] **Step 6: Implement EnvironmentSecretsService**

```csharp
// src/MSOSync.Secrets/EnvironmentSecretsService.cs
using Microsoft.Extensions.Configuration;

namespace MSOSync.Secrets;

internal sealed class EnvironmentSecretsService : ISecretsService
{
    private readonly IConfiguration _config;
    private readonly bool _isProduction;

    public EnvironmentSecretsService(IConfiguration config, bool isProduction)
    {
        _config = config;
        _isProduction = isProduction;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        // Try env var first: replace : with __ (double underscore)
        var envKey = key.Replace(":", "__").ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(envKey);

        // Also try the legacy MSOSYNC_ prefix form for backward compat
        if (value is null)
            value = Environment.GetEnvironmentVariable("MSOSYNC_" + envKey);

        // In non-production environments, fall back to IConfiguration
        if (value is null && !_isProduction)
            value = _config[key];

        return Task.FromResult(value);
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return value is null ? null : System.Text.Encoding.UTF8.GetBytes(value);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;
}
```

- [ ] **Step 7: Run tests — expect all pass**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: `Passed: 6, Failed: 0`

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Secrets/MSOSync.Secrets.csproj src/MSOSync.Secrets/ISecretsService.cs src/MSOSync.Secrets/EnvironmentSecretsService.cs tests/MSOSync.SecretsTests/MSOSync.SecretsTests.csproj tests/MSOSync.SecretsTests/EnvironmentSecretsServiceTests.cs MSOSync.sln
git commit -m "feat(2E.1-T1): add ISecretsService + EnvironmentSecretsService"
```

---

### Task 2: CompositeSecretsService + SecretsOptions config model

**Files:**
- Create: `src/MSOSync.Secrets/CompositeSecretsService.cs`
- Create: `src/MSOSync.Secrets/SecretsOptions.cs`
- Modify: `tests/MSOSync.SecretsTests/CompositeSecretsServiceTests.cs` (new file)

**Interfaces:**
- Consumes: `ISecretsService` (Task 1)
- Produces: `CompositeSecretsService(IEnumerable<ISecretsService> providers)`, `SecretsOptions` with `Provider` string and `AzureKeyVault` nested options

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.SecretsTests/CompositeSecretsServiceTests.cs
using FluentAssertions;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class CompositeSecretsServiceTests
{
    [Fact]
    public async Task GetSecretAsync_ReturnsFirstNonNull()
    {
        var first = new StubSecretsService("key", null);
        var second = new StubSecretsService("key", "found");
        var composite = new CompositeSecretsService([first, second]);

        var result = await composite.GetSecretAsync("key");

        result.Should().Be("found");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenAllProvidersReturnNull()
    {
        var composite = new CompositeSecretsService([new StubSecretsService("key", null)]);

        var result = await composite.GetSecretAsync("other");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsFirst_WhenMultipleMatch()
    {
        var first = new StubSecretsService("key", "first-value");
        var second = new StubSecretsService("key", "second-value");
        var composite = new CompositeSecretsService([first, second]);

        var result = await composite.GetSecretAsync("key");

        result.Should().Be("first-value");
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenNotFound()
    {
        var composite = new CompositeSecretsService([new StubSecretsService("key", null)]);

        var exists = await composite.ExistsAsync("missing");

        exists.Should().BeFalse();
    }

    private sealed class StubSecretsService(string key, string? value) : ISecretsService
    {
        public Task<string?> GetSecretAsync(string k, CancellationToken ct = default)
            => Task.FromResult(k == key ? value : null);
        public Task<byte[]?> GetSecretBytesAsync(string k, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
        public Task<bool> ExistsAsync(string k, CancellationToken ct = default)
            => Task.FromResult(k == key && value is not null);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.SecretsTests -v minimal 2>&1 | head -5
```

- [ ] **Step 3: Implement SecretsOptions**

```csharp
// src/MSOSync.Secrets/SecretsOptions.cs
namespace MSOSync.Secrets;

public sealed class SecretsOptions
{
    public const string Section = "Secrets";

    public string Provider { get; set; } = "Environment";

    public AzureKeyVaultOptions AzureKeyVault { get; set; } = new();
}

public sealed class AzureKeyVaultOptions
{
    public string VaultUri { get; set; } = string.Empty;
    public int CacheTtlSeconds { get; set; } = 300;
}
```

- [ ] **Step 4: Implement CompositeSecretsService**

```csharp
// src/MSOSync.Secrets/CompositeSecretsService.cs
namespace MSOSync.Secrets;

internal sealed class CompositeSecretsService(IEnumerable<ISecretsService> providers) : ISecretsService
{
    private readonly IReadOnlyList<ISecretsService> _providers = providers.ToList();

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var value = await provider.GetSecretAsync(key, ct);
            if (value is not null) return value;
        }
        return null;
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var value = await provider.GetSecretBytesAsync(key, ct);
            if (value is not null) return value;
        }
        return null;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;
}
```

- [ ] **Step 5: Run tests — all pass**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: `Passed: 10, Failed: 0`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Secrets/CompositeSecretsService.cs src/MSOSync.Secrets/SecretsOptions.cs tests/MSOSync.SecretsTests/CompositeSecretsServiceTests.cs
git commit -m "feat(2E.1-T2): add CompositeSecretsService + SecretsOptions"
```

---

### Task 3: DI registration + migrate existing secret reads

**Files:**
- Create: `src/MSOSync.Secrets/SecretsServiceExtensions.cs`
- Modify: `src/MSOSync.App/MSOSync.App.csproj` (add project reference)
- Modify: `src/MSOSync.App/Program.cs` (register ISecretsService before security)
- Modify: `src/MSOSync.Security/SecurityServiceExtensions.cs` (consume ISecretsService)
- Modify: `src/MSOSync.App/appsettings.json` (add Secrets section)

**Interfaces:**
- Consumes: `ISecretsService`, `SecretsOptions`, `CompositeSecretsService`, `EnvironmentSecretsService` (Tasks 1–2)
- Produces: `AddSecretsService(IServiceCollection, IConfiguration)` extension method

- [ ] **Step 1: Write AddSecretsService extension**

```csharp
// src/MSOSync.Secrets/SecretsServiceExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MSOSync.Secrets;

public static class SecretsServiceExtensions
{
    public static IServiceCollection AddSecretsService(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        services.AddOptions<SecretsOptions>()
            .BindConfiguration(SecretsOptions.Section)
            .Validate(o => o.Provider is "Environment" or "AzureKeyVault",
                "Secrets:Provider must be 'Environment' or 'AzureKeyVault'")
            .ValidateOnStart();

        var opts = config.GetSection(SecretsOptions.Section).Get<SecretsOptions>() ?? new();

        // EnvironmentSecretsService always in chain
        services.AddSingleton<ISecretsService>(
            new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment()));

        // CompositeSecretsService wraps all registered ISecretsService implementations
        // AzureKeyVaultSecretsService (if configured) prepended in 2E.2
        services.AddSingleton<CompositeSecretsService>(sp =>
            new CompositeSecretsService(sp.GetServices<ISecretsService>()));

        // Re-register composite as the primary ISecretsService (last registration wins for single-instance resolution)
        // Use factory to break circular — consumers get CompositeSecretsService through this
        services.AddSingleton<ISecretsService>(sp => sp.GetRequiredService<CompositeSecretsService>());

        return services;
    }
}
```

Wait — the registration above has an issue: registering `ISecretsService` twice means `GetRequiredService<ISecretsService>()` returns the composite (last registration), but `GetServices<ISecretsService>()` returns both (env + composite), causing circular dependency.

**Correct pattern:** Use keyed services or explicit ordering. Here is the corrected implementation:

```csharp
// src/MSOSync.Secrets/SecretsServiceExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MSOSync.Secrets;

public static class SecretsServiceExtensions
{
    public static IServiceCollection AddSecretsService(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        services.AddOptions<SecretsOptions>()
            .BindConfiguration(SecretsOptions.Section)
            .Validate(o => o.Provider is "Environment" or "AzureKeyVault",
                "Secrets:Provider must be 'Environment' or 'AzureKeyVault'")
            .ValidateOnStart();

        services.AddSingleton<EnvironmentSecretsService>(sp =>
            new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment()));

        services.AddSingleton<ISecretsService>(sp =>
        {
            // Providers in resolution order: Azure KV (if registered) → env
            // AzureKeyVaultSecretsService added to the chain in 2E.2 by prepending before this factory runs
            var providers = new List<ISecretsService>
            {
                sp.GetRequiredService<EnvironmentSecretsService>()
            };
            return new CompositeSecretsService(providers);
        });

        return services;
    }
}
```

- [ ] **Step 2: Add Secrets section to appsettings.json**

Open `src/MSOSync.App/appsettings.json`. Add after the existing top-level sections:

```json
"Secrets": {
  "Provider": "Environment"
}
```

- [ ] **Step 3: Add project reference to MSOSync.App**

```xml
<!-- In src/MSOSync.App/MSOSync.App.csproj, inside <ItemGroup> with other project refs -->
<ProjectReference Include="..\MSOSync.Secrets\MSOSync.Secrets.csproj" />
```

- [ ] **Step 4: Register ISecretsService in Program.cs**

Open `src/MSOSync.App/Program.cs`. Find where `AddSecurity()` is called (around line 68). Add `AddSecretsService` call BEFORE `AddSecurity`:

```csharp
// Add before: builder.Services.AddSecurity(config)  (around line 68)
builder.Services.AddSecretsService(builder.Configuration, builder.Environment);
```

Add the using at the top:
```csharp
using MSOSync.Secrets;
```

- [ ] **Step 5: Update SecurityServiceExtensions to consume ISecretsService**

Open `src/MSOSync.Security/SecurityServiceExtensions.cs`. Add `MSOSync.Secrets` project reference to `MSOSync.Security.csproj`:

```xml
<ProjectReference Include="..\MSOSync.Secrets\MSOSync.Secrets.csproj" />
```

Update the JWT secret reading. Find where `MSOSYNC_JWT_SECRET` is read (around line 21-28 per prior audit). Change from:
```csharp
var jwtSecret = Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET")
    ?? throw new InvalidOperationException("MSOSYNC_JWT_SECRET is required");
```
To use `ISecretsService` via a startup-time read. Because `AddSecurity` is called synchronously at startup, use a synchronous wrapper:
```csharp
// Inject ISecretsService to read JWT secret at options configuration time
services.AddOptions<JwtOptions>()
    .Configure<ISecretsService>((opts, secrets) =>
    {
        // Read synchronously at startup — acceptable for one-time config binding
        opts.SigningKey = secrets.GetSecretAsync("Jwt:SigningKey").GetAwaiter().GetResult()
            ?? secrets.GetSecretAsync("MSOSYNC_JWT_SECRET").GetAwaiter().GetResult()
            ?? throw new InvalidOperationException(
                "JWT signing key not found. Set MSOSYNC_JWT_SECRET or Secrets:Jwt:SigningKey.");
    });
```

Note: If `JwtOptions` is currently bound directly from config (not via `ISecretsService`), find the existing `Configure<JwtOptions>` call and modify it to also inject `ISecretsService`. Read the current `SecurityServiceExtensions.cs` to confirm the exact binding pattern before editing.

- [ ] **Step 6: Build and verify no compile errors**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -10
```
Expected: `Build succeeded.`

- [ ] **Step 7: Run tests**

```
dotnet test tests/MSOSync.SecretsTests tests/MSOSync.SecurityTests -v minimal 2>&1 | tail -10
```
Expected: all pass (no regressions in SecurityTests).

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Secrets/SecretsServiceExtensions.cs src/MSOSync.App/MSOSync.App.csproj src/MSOSync.App/Program.cs src/MSOSync.App/appsettings.json src/MSOSync.Security/SecurityServiceExtensions.cs src/MSOSync.Security/MSOSync.Security.csproj
git commit -m "feat(2E.1-T3): register ISecretsService in DI, migrate JWT secret read"
```

---

### Task 4: Migrate HMAC cursor key + node bootstrap token; integration smoke test

**Files:**
- Modify: `src/MSOSync.Metadata/Services/NodeMetadataService.cs` (cursor HMAC key via ISecretsService)
- Modify: `src/MSOSync.App/Program.cs` (node bootstrap token via ISecretsService)
- Create: `tests/MSOSync.SecretsTests/SecretsIntegrationTests.cs`

**Interfaces:**
- Consumes: `ISecretsService` (Task 1), registered in Program.cs (Task 3)

- [ ] **Step 1: Write smoke test for secret resolution chain**

```csharp
// tests/MSOSync.SecretsTests/SecretsIntegrationTests.cs
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class SecretsIntegrationTests : IDisposable
{
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("INTEGRATION_TEST_KEY__SECRET", null);
    }

    [Fact]
    public async Task CompositeResolves_EnvVar_WhenSet()
    {
        Environment.SetEnvironmentVariable("INTEGRATION_TEST_KEY__SECRET", "env-value");
        var envSvc = new EnvironmentSecretsService(new ConfigurationBuilder().Build(), isProduction: true);
        var composite = new CompositeSecretsService([envSvc]);

        var result = await composite.GetSecretAsync("INTEGRATION_TEST_KEY:SECRET");

        result.Should().Be("env-value");
    }

    [Fact]
    public async Task Composite_ReturnsNull_WhenKeyMissing()
    {
        var envSvc = new EnvironmentSecretsService(new ConfigurationBuilder().Build(), isProduction: true);
        var composite = new CompositeSecretsService([envSvc]);

        var result = await composite.GetSecretAsync("DEFINITELY_MISSING_KEY_XYZ");

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run smoke tests — all pass**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: `Passed: 12, Failed: 0`

- [ ] **Step 3: Migrate HMAC cursor key**

Open `src/MSOSync.Metadata/Services/NodeMetadataService.cs`. Find where `IConfiguration` or direct config reads the `Pagination:CursorHmacKey`. Add `ISecretsService` constructor injection:

```csharp
// Add to NodeMetadataService constructor parameters:
private readonly ISecretsService _secrets;

// In the constructor body, change the HMAC key read:
// OLD: var hmacKey = _config["Pagination:CursorHmacKey"] ?? throw ...
// NEW: (call at usage point, or store in field at construction)
private string GetHmacKey() =>
    _secrets.GetSecretAsync("Pagination:CursorHmacKey").GetAwaiter().GetResult()
    ?? throw new InvalidOperationException("Pagination:CursorHmacKey secret not found.");
```

Add `MSOSync.Secrets` project reference to `MSOSync.Metadata.csproj`.

- [ ] **Step 4: Migrate node bootstrap token**

Open `src/MSOSync.App/Program.cs`. Find where `MSOSYNC_NODE_TOKEN` is read (around line 43-56 per prior audit). Change to:

```csharp
// OLD:
// var nodeToken = Environment.GetEnvironmentVariable("MSOSYNC_NODE_TOKEN")
//     ?? throw new InvalidOperationException("...");
// NEW (after ISecretsService is registered):
var secrets = app.Services.GetRequiredService<ISecretsService>();
var nodeToken = await secrets.GetSecretAsync("Node:BootstrapToken")
    ?? await secrets.GetSecretAsync("MSOSYNC_NODE_TOKEN")
    ?? throw new InvalidOperationException(
        "Node bootstrap token not found. Set MSOSYNC_NODE_TOKEN env var.");
```

Note: if the current code reads the token in the `builder` phase (before `app.Build()`), it can use `ISecretsService` directly from `builder.Services` by building a temporary scope. However, the simpler approach is to read it in the `app` phase after `Build()` completes. Read `Program.cs` to confirm where the token is read and adjust accordingly.

- [ ] **Step 5: Full build + all existing tests**

```
dotnet build D:\MSOSync\MSOSync.sln 2>&1 | tail -5
dotnet test tests/MSOSync.SecretsTests tests/MSOSync.MetadataTests tests/MSOSync.SecurityTests -v minimal 2>&1 | tail -10
```
Expected: build succeeds, all tests pass.

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Metadata/Services/NodeMetadataService.cs src/MSOSync.Metadata/MSOSync.Metadata.csproj src/MSOSync.App/Program.cs tests/MSOSync.SecretsTests/SecretsIntegrationTests.cs
git commit -m "feat(2E.1-T4): migrate HMAC cursor key + node token to ISecretsService"
```
