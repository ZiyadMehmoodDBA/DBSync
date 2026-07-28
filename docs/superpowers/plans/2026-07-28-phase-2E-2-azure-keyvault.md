# Phase 2E.2 — Azure Key Vault Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `AzureKeyVaultSecretsService` to the secrets provider chain — when `Secrets:Provider = "AzureKeyVault"`, Azure Key Vault is consulted first with TTL-based caching; environment variables remain the fallback.

**Architecture:** `AzureKeyVaultSecretsService` wraps `SecretClient` with `DefaultAzureCredential`. Key names map `:` → `-`. Results cached in `IMemoryCache` with configurable TTL. A `KeyVaultHealthContributor` reports `Degraded` (not `Unhealthy`) so the app starts without vault — env vars fall through.

**Tech Stack:** C# 13 / .NET 9 / Azure.Security.KeyVault.Secrets / Azure.Identity / Microsoft.Extensions.Caching.Memory

## Global Constraints

- Prerequisite: 2E.1 complete — `ISecretsService`, `SecretsOptions`, `CompositeSecretsService` exist
- `AzureKeyVaultSecretsService` is `internal sealed`
- Key name mapping: replace `:` and `.` with `-` (Key Vault only allows alphanumeric and `-`)
- Returns `null` (never throws) when secret not found (HTTP 404) — throws on auth failures or network errors
- Cache TTL from `SecretsOptions.AzureKeyVault.CacheTtlSeconds` (default 300)
- Health contributor reports `Degraded` (not `Unhealthy`) on vault unreachable — app still starts
- `git add` by file name only

---

### Task 1: AzureKeyVaultSecretsService implementation

**Files:**
- Create: `src/MSOSync.Secrets/AzureKeyVaultSecretsService.cs`
- Modify: `src/MSOSync.Secrets/MSOSync.Secrets.csproj` (add Azure packages)
- Create: `tests/MSOSync.SecretsTests/AzureKeyVaultSecretsServiceTests.cs`

**Interfaces:**
- Consumes: `ISecretsService` (2E.1), `SecretsOptions.AzureKeyVault`
- Produces: `AzureKeyVaultSecretsService(SecretClient, IMemoryCache, AzureKeyVaultOptions)`

- [ ] **Step 1: Add Azure packages to MSOSync.Secrets.csproj**

```xml
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />
<PackageReference Include="Azure.Identity" Version="1.13.2" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />
```

- [ ] **Step 2: Write failing tests**

```csharp
// tests/MSOSync.SecretsTests/AzureKeyVaultSecretsServiceTests.cs
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class AzureKeyVaultSecretsServiceTests
{
    private static AzureKeyVaultSecretsService Build(SecretClient client)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new AzureKeyVaultSecretsService(client, cache,
            new AzureKeyVaultOptions { VaultUri = "https://test.vault.azure.net/", CacheTtlSeconds = 60 });
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenSecretExists()
    {
        var mock = new Mock<SecretClient>();
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("Jwt--SigningKey"), "my-jwt-secret");
        mock.Setup(c => c.GetSecretAsync("Jwt--SigningKey", null, default))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var svc = Build(mock.Object);
        var result = await svc.GetSecretAsync("Jwt:SigningKey");

        result.Should().Be("my-jwt-secret");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenSecretNotFound()
    {
        var mock = new Mock<SecretClient>();
        mock.Setup(c => c.GetSecretAsync(It.IsAny<string>(), null, default))
            .ThrowsAsync(new RequestFailedException(404, "Secret not found"));

        var svc = Build(mock.Object);
        var result = await svc.GetSecretAsync("Missing:Key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsCachedValue_OnSecondCall()
    {
        var mock = new Mock<SecretClient>();
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("Cached--Key"), "cached-value");
        mock.Setup(c => c.GetSecretAsync("Cached--Key", null, default))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var svc = Build(mock.Object);
        await svc.GetSecretAsync("Cached:Key");
        await svc.GetSecretAsync("Cached:Key");

        // SecretClient called only once; second call served from cache
        mock.Verify(c => c.GetSecretAsync("Cached--Key", null, default), Times.Once);
    }

    [Fact]
    public async Task GetSecretAsync_MapsColonToDash_InKeyName()
    {
        var mock = new Mock<SecretClient>();
        mock.Setup(c => c.GetSecretAsync("Jwt--SigningKey", null, default))
            .ReturnsAsync(Response.FromValue(
                SecretModelFactory.KeyVaultSecret(new SecretProperties("Jwt--SigningKey"), "val"),
                Mock.Of<Response>()));

        var svc = Build(mock.Object);
        await svc.GetSecretAsync("Jwt:SigningKey");

        mock.Verify(c => c.GetSecretAsync("Jwt--SigningKey", null, default), Times.Once);
    }
}
```

Add `Moq` to test project csproj if not present: `<PackageReference Include="Moq" Version="4.20.72" />`

- [ ] **Step 3: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.SecretsTests -v minimal 2>&1 | head -5
```

- [ ] **Step 4: Implement AzureKeyVaultSecretsService**

```csharp
// src/MSOSync.Secrets/AzureKeyVaultSecretsService.cs
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Secrets;

internal sealed class AzureKeyVaultSecretsService : ISecretsService
{
    private readonly SecretClient _client;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl;

    public AzureKeyVaultSecretsService(
        SecretClient client,
        IMemoryCache cache,
        AzureKeyVaultOptions options)
    {
        _client = client;
        _cache = cache;
        _cacheTtl = TimeSpan.FromSeconds(options.CacheTtlSeconds);
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var vaultKey = MapKey(key);
        if (_cache.TryGetValue<string?>(vaultKey, out var cached)) return cached;

        try
        {
            var response = await _client.GetSecretAsync(vaultKey, version: null, ct);
            var value = response.Value.Value;
            _cache.Set(vaultKey, value, _cacheTtl);
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.Set<string?>(vaultKey, null, TimeSpan.FromSeconds(30)); // brief negative cache
            return null;
        }
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return value is null ? null : System.Text.Encoding.UTF8.GetBytes(value);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;

    private static string MapKey(string key)
        => key.Replace(":", "--").Replace(".", "-");
}
```

- [ ] **Step 5: Run tests — all pass**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: `Passed: 16+, Failed: 0`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Secrets/AzureKeyVaultSecretsService.cs src/MSOSync.Secrets/MSOSync.Secrets.csproj tests/MSOSync.SecretsTests/AzureKeyVaultSecretsServiceTests.cs
git commit -m "feat(2E.2-T1): add AzureKeyVaultSecretsService with TTL caching"
```

---

### Task 2: KeyVaultHealthContributor + DI registration

**Files:**
- Create: `src/MSOSync.Secrets/KeyVaultHealthContributor.cs`
- Modify: `src/MSOSync.Secrets/SecretsServiceExtensions.cs` (register Azure KV when configured)

**Interfaces:**
- Consumes: `ISystemHealthContributor` from `MSOSync.Common`, `AzureKeyVaultSecretsService` (Task 1)

- [ ] **Step 1: Write KeyVaultHealthContributor**

```csharp
// src/MSOSync.Secrets/KeyVaultHealthContributor.cs
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Common.Health;

namespace MSOSync.Secrets;

internal sealed class KeyVaultHealthContributor(SecretClient client) : ISystemHealthContributor
{
    public string Name => "AzureKeyVault";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(ct).AsPages().WithCancellation(ct))
                break; // Just needs to enumerate one page successfully
            return HealthCheckResult.Healthy("Azure Key Vault reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Azure Key Vault unreachable: {ex.Message}");
        }
    }
}
```

Note: if `ISystemHealthContributor` uses a different signature in the existing codebase, read `src/MSOSync.Common/Health/ISystemHealthContributor.cs` and match it exactly.

- [ ] **Step 2: Update SecretsServiceExtensions to register Azure KV**

Open `src/MSOSync.Secrets/SecretsServiceExtensions.cs`. Update `AddSecretsService` to prepend `AzureKeyVaultSecretsService` when `Provider == "AzureKeyVault"`:

```csharp
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

    services.AddMemoryCache();
    services.AddSingleton<EnvironmentSecretsService>(sp =>
        new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment()));

    if (opts.Provider == "AzureKeyVault")
    {
        services.AddSingleton<SecretClient>(sp =>
            new SecretClient(new Uri(opts.AzureKeyVault.VaultUri), new Azure.Identity.DefaultAzureCredential()));
        services.AddSingleton<AzureKeyVaultSecretsService>(sp =>
            new AzureKeyVaultSecretsService(
                sp.GetRequiredService<SecretClient>(),
                sp.GetRequiredService<IMemoryCache>(),
                opts.AzureKeyVault));
        services.AddSingleton<ISystemHealthContributor, KeyVaultHealthContributor>();

        services.AddSingleton<ISecretsService>(sp => new CompositeSecretsService([
            sp.GetRequiredService<AzureKeyVaultSecretsService>(),
            sp.GetRequiredService<EnvironmentSecretsService>()
        ]));
    }
    else
    {
        services.AddSingleton<ISecretsService>(sp => new CompositeSecretsService([
            sp.GetRequiredService<EnvironmentSecretsService>()
        ]));
    }

    return services;
}
```

Add `using Azure.Security.KeyVault.Secrets; using Azure.Identity; using MSOSync.Common.Health;` at top of file.
Add `MSOSync.Common` project reference to `MSOSync.Secrets.csproj`.

- [ ] **Step 3: Build**

```
dotnet build src/MSOSync.Secrets/MSOSync.Secrets.csproj 2>&1 | tail -5
```
Expected: `Build succeeded.`

- [ ] **Step 4: Run all secrets tests**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: all pass.

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Secrets/KeyVaultHealthContributor.cs src/MSOSync.Secrets/SecretsServiceExtensions.cs src/MSOSync.Secrets/MSOSync.Secrets.csproj
git commit -m "feat(2E.2-T2): add KeyVaultHealthContributor + DI wiring for AzureKeyVault provider"
```

---

### Task 3: Integration test (skipped in CI) + appsettings documentation

**Files:**
- Create: `tests/MSOSync.SecretsTests/AzureKeyVaultIntegrationTests.cs`
- Modify: `src/MSOSync.App/appsettings.json` (document AzureKeyVault config block)

- [ ] **Step 1: Write skip-able integration test**

```csharp
// tests/MSOSync.SecretsTests/AzureKeyVaultIntegrationTests.cs
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

[Trait("Category", "Integration")]
public sealed class AzureKeyVaultIntegrationTests
{
    private static readonly string? VaultUri = Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URI");

    [SkippableFact]
    public async Task GetSecretAsync_ReturnsValue_FromRealVault()
    {
        Skip.If(string.IsNullOrEmpty(VaultUri), "AZURE_KEY_VAULT_URI not set — skipping vault integration test.");

        var client = new SecretClient(new Uri(VaultUri!), new DefaultAzureCredential());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new AzureKeyVaultOptions { VaultUri = VaultUri!, CacheTtlSeconds = 60 };
        var svc = new AzureKeyVaultSecretsService(client, cache, opts);

        // Requires a secret named "Test--IntegrationKey" in the vault with value "integration-ok"
        var result = await svc.GetSecretAsync("Test:IntegrationKey");

        result.Should().Be("integration-ok");
    }
}
```

Add `xunit.SkippableFact` package: `<PackageReference Include="Xunit.SkippableFact" Version="1.4.13" />`

- [ ] **Step 2: Add AzureKeyVault config block to appsettings.json (commented example)**

```json
"Secrets": {
  "Provider": "Environment",
  "AzureKeyVault": {
    "VaultUri": "",
    "CacheTtlSeconds": 300
  }
}
```

Update the existing `Secrets` section to include the nested `AzureKeyVault` block.

- [ ] **Step 3: Run tests (integration test should skip)**

```
dotnet test tests/MSOSync.SecretsTests -v minimal
```
Expected: all pass (integration test skipped with `SKIP` status if `AZURE_KEY_VAULT_URI` not set).

- [ ] **Step 4: Commit**

```
git add tests/MSOSync.SecretsTests/AzureKeyVaultIntegrationTests.cs tests/MSOSync.SecretsTests/MSOSync.SecretsTests.csproj src/MSOSync.App/appsettings.json
git commit -m "feat(2E.2-T3): add skip-able Azure KV integration test + appsettings documentation"
```
