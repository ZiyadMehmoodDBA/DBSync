# Phase 2C.2 — Plugin Marketplace Backend: Design Specification

**Date:** 2026-07-23
**Status:** Approved
**Phase:** 2C — SDK & Ecosystem
**Prerequisite:** Phase 2C.1 (Plugin Packaging & Installer) must be complete. `IPluginInstaller` is consumed here but defined there.

---

## Goal

Expose a hosted registry catalog of plugins that MSOSync administrators can search, inspect, and install directly from the admin UI or API — without manually copying `.msopkg` files. The marketplace is an optional capability: when unconfigured it degrades gracefully to 503. All write operations (install, update) delegate to `IPluginInstaller` from 2C.1, keeping the install pipeline in one place.

---

## Architecture

```
MSOSync.Api
  Controllers/
    MarketplaceController.cs          ← new: all marketplace endpoints
  Dtos/
    Marketplace/
      MarketplacePluginListItemDto.cs
      MarketplacePluginDetailDto.cs
      MarketplaceVersionDto.cs
      MarketplaceInstallRequest.cs
      MarketplaceInstallResult.cs
      MarketplaceUpdateManifestDto.cs
      BulkUpdateCheckRequest.cs
      BulkUpdateCheckResult.cs
      MarketplaceSearchParams.cs

MSOSync.Plugin (existing project — new files only)
  Marketplace/
    IMarketplaceService.cs            ← new: remote catalog operations
    IPluginUpdateService.cs           ← new: update check logic
    MarketplaceOptions.cs             ← new: IOptions<MarketplaceOptions>
    Models/
      RegistryPluginEntry.cs          ← remote registry JSON model
      RegistryVersionEntry.cs
      RegistrySearchResult.cs

MSOSync.Persistence
  Entities/
    SyncMarketplaceCache.cs           ← new entity
  Configurations/
    SyncMarketplaceCacheConfiguration.cs
  Migrations/
    M035_MarketplaceCache.cs
  Stores/
    MarketplaceCacheStore.cs          ← implements IMarketplaceCacheStore

MSOSync.Metadata (or MSOSync.Plugin.Infrastructure — same project pattern as 2C.1)
  Marketplace/
    MarketplaceService.cs             ← implements IMarketplaceService
    PluginUpdateService.cs            ← implements IPluginUpdateService
    IMarketplaceCacheStore.cs         ← bridge to persistence layer

MSOSync.App
  ServiceCollectionExtensions:
    AddMarketplace(services, config)  ← wires all new registrations
```

**Dependency rule:** `MSOSync.Plugin` defines `IMarketplaceService`, `IPluginUpdateService`, `MarketplaceOptions`, and all marketplace models. It does NOT reference `MSOSync.Persistence`. Implementations live in `MSOSync.Metadata` (or the same infrastructure project used by 2C.1). `MSOSync.App` wires everything. This mirrors the `IPluginStore` / `PluginStore` split established in Epic 14A.

---

## Configuration — `MarketplaceOptions`

**File:** `src/MSOSync.Plugin/Marketplace/MarketplaceOptions.cs`

```csharp
namespace MSOSync.Plugin.Marketplace;

public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>
    /// Base URL of the remote registry. Example: https://marketplace.msosync.io/api/v1
    /// When null or empty, all marketplace endpoints return 503.
    /// </summary>
    public string? RegistryUrl { get; set; }

    /// <summary>
    /// Optional API key sent in the X-Api-Key header for authenticated registry access.
    /// Leave null for public registries.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Minutes to retain remote registry results in the local DB cache.
    /// Default: 60. Zero disables DB caching (always fetches from remote).
    /// </summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>
    /// Minutes to retain search results in IMemoryCache (short-term, in-process).
    /// Default: 5. Independent of DB cache.
    /// </summary>
    public int MemoryCacheMinutes { get; set; } = 5;

    /// <summary>
    /// Timeout in seconds for HTTP calls to the registry. Default: 30.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of Polly retry attempts on transient HTTP failures. Default: 3.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RegistryUrl);
}
```

**`appsettings.json` section (added to existing file):**

```json
"Marketplace": {
  "RegistryUrl": "",
  "ApiKey": "",
  "CacheMinutes": 60,
  "MemoryCacheMinutes": 5,
  "HttpTimeoutSeconds": 30,
  "RetryCount": 3
}
```

---

## Data Model — `SyncMarketplaceCache`

**File:** `src/MSOSync.Persistence/Entities/SyncMarketplaceCache.cs`

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

/// <summary>
/// Local cache of remote marketplace registry entries.
/// One row per plugin ID per registry source. Avoids hitting the remote on every request.
/// </summary>
[GlobalEntity]
public sealed class SyncMarketplaceCache
{
    /// <summary>Surrogate PK (int identity for fast seek).</summary>
    public int Id { get; set; }

    /// <summary>Registry base URL (normalized, trailing slash stripped). Identifies the source registry.</summary>
    public string RegistryUrl { get; set; } = null!;

    /// <summary>Plugin ID as returned by the registry (e.g. msosync.sqlserver.collector).</summary>
    public string PluginId { get; set; } = null!;

    /// <summary>Latest version string from the registry at cache time.</summary>
    public string LatestVersion { get; set; } = null!;

    /// <summary>JSON-serialized RegistryPluginEntry — full metadata blob, avoids column explosion.</summary>
    public string MetadataJson { get; set; } = null!;

    /// <summary>UTC timestamp when this cache entry was written or refreshed.</summary>
    public DateTime CachedAt { get; set; }

    /// <summary>UTC timestamp after which this entry is considered stale and must be re-fetched.</summary>
    public DateTime ExpiresAt { get; set; }
}
```

**Entity configuration:** `src/MSOSync.Persistence/Configurations/SyncMarketplaceCacheConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncMarketplaceCacheConfiguration : IEntityTypeConfiguration<SyncMarketplaceCache>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncMarketplaceCache> builder)
    {
        builder.ToTable("sync_marketplace_cache", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

        builder.Property(e => e.RegistryUrl)
            .HasColumnName("registry_url")
            .HasColumnType("nvarchar(500)")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.PluginId)
            .HasColumnName("plugin_id")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.LatestVersion)
            .HasColumnName("latest_version")
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(e => e.CachedAt)
            .HasColumnName("cached_at")
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("datetime2")
            .IsRequired();

        // Composite unique index: one cache entry per (registry, pluginId)
        builder.HasIndex(e => new { e.RegistryUrl, e.PluginId })
            .IsUnique()
            .HasDatabaseName("IX_sync_marketplace_cache_registry_plugin");

        // Index for expiry-based sweep (background purge or on-read staleness check)
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_sync_marketplace_cache_expires_at");
    }
}
```

**Migration:** `M035_MarketplaceCache` — adds `msosync.sync_marketplace_cache` with the columns and indexes above. Table count goes from 44 → 45. Update any schema-count assertion in `PersistenceTests` accordingly.

**`AppDbContext` addition:**

```csharp
public DbSet<SyncMarketplaceCache> MarketplaceCache => Set<SyncMarketplaceCache>();
```

---

## Remote Registry Models

These are deserialization targets for the remote registry JSON API. They live in `MSOSync.Plugin` so the service interface can reference them without pulling in infrastructure concerns.

**File:** `src/MSOSync.Plugin/Marketplace/Models/RegistryPluginEntry.cs`

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>
/// Represents a single plugin entry returned by the remote registry catalog.
/// Matches the registry JSON contract; fields map 1-to-1 with registry API responses.
/// </summary>
public sealed record RegistryPluginEntry
{
    [JsonPropertyName("id")]            public string   Id            { get; init; } = null!;
    [JsonPropertyName("name")]          public string   Name          { get; init; } = null!;
    [JsonPropertyName("author")]        public string   Author        { get; init; } = null!;
    [JsonPropertyName("description")]   public string   Description   { get; init; } = null!;
    [JsonPropertyName("category")]      public string   Category      { get; init; } = null!;
    [JsonPropertyName("tags")]          public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("latestVersion")] public string   LatestVersion { get; init; } = null!;
    [JsonPropertyName("minHostVersion")]public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("downloadCount")] public long     DownloadCount { get; init; }
    [JsonPropertyName("rating")]        public double   Rating        { get; init; }
    [JsonPropertyName("ratingCount")]   public int      RatingCount   { get; init; }
    [JsonPropertyName("publishedAt")]   public DateTime PublishedAt   { get; init; }
    [JsonPropertyName("updatedAt")]     public DateTime UpdatedAt     { get; init; }
    [JsonPropertyName("iconUrl")]       public string?  IconUrl       { get; init; }
    [JsonPropertyName("projectUrl")]    public string?  ProjectUrl    { get; init; }
    [JsonPropertyName("licenseId")]     public string?  LicenseId     { get; init; }
    [JsonPropertyName("verified")]      public bool     Verified      { get; init; }
    [JsonPropertyName("versions")]      public IReadOnlyList<RegistryVersionEntry> Versions { get; init; } = [];
}
```

**File:** `src/MSOSync.Plugin/Marketplace/Models/RegistryVersionEntry.cs`

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

public sealed record RegistryVersionEntry
{
    [JsonPropertyName("version")]       public string   Version       { get; init; } = null!;
    [JsonPropertyName("minHostVersion")]public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]public string   MaxHostVersion { get; init; } = null!;
    [JsonPropertyName("publishedAt")]   public DateTime PublishedAt   { get; init; }
    [JsonPropertyName("downloadUrl")]   public string   DownloadUrl   { get; init; } = null!;
    [JsonPropertyName("sha256")]        public string   Sha256        { get; init; } = null!;
    [JsonPropertyName("releaseNotes")]  public string?  ReleaseNotes  { get; init; }
    [JsonPropertyName("deprecated")]    public bool     Deprecated    { get; init; }
}
```

**File:** `src/MSOSync.Plugin/Marketplace/Models/RegistrySearchResult.cs`

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>
/// Paged search result envelope returned by the remote registry search endpoint.
/// </summary>
public sealed record RegistrySearchResult
{
    [JsonPropertyName("data")]       public IReadOnlyList<RegistryPluginEntry> Data       { get; init; } = [];
    [JsonPropertyName("total")]      public int Total      { get; init; }
    [JsonPropertyName("page")]       public int Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int PageSize   { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
}
```

---

## Service Interfaces

### `IMarketplaceCacheStore`

**File:** `src/MSOSync.Plugin/Marketplace/IMarketplaceCacheStore.cs`

```csharp
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Persistence layer bridge for the marketplace DB cache.
/// Implemented in MSOSync.Persistence. Injected via DI (Scoped).
/// </summary>
public interface IMarketplaceCacheStore
{
    /// <summary>
    /// Returns all non-expired cache entries for the given registry URL.
    /// Returns null when no valid cache entry exists.
    /// </summary>
    Task<IReadOnlyList<RegistryPluginEntry>?> GetSearchCacheAsync(
        string registryUrl,
        string cacheKey,
        CancellationToken ct);

    /// <summary>
    /// Returns a single non-expired cache entry for the given plugin ID.
    /// Returns null when no valid entry exists or the entry is expired.
    /// </summary>
    Task<RegistryPluginEntry?> GetPluginCacheAsync(
        string registryUrl,
        string pluginId,
        CancellationToken ct);

    /// <summary>
    /// Upserts a cache entry for a single plugin. Sets ExpiresAt based on CacheMinutes.
    /// </summary>
    Task UpsertAsync(
        string registryUrl,
        RegistryPluginEntry entry,
        int cacheMinutes,
        CancellationToken ct);

    /// <summary>
    /// Bulk upsert — used after a search response to cache all returned entries.
    /// Must not use Task.WhenAll on a shared DbContext; iterate sequentially.
    /// </summary>
    Task UpsertBulkAsync(
        string registryUrl,
        IReadOnlyList<RegistryPluginEntry> entries,
        int cacheMinutes,
        CancellationToken ct);

    /// <summary>
    /// Deletes all expired rows for all registry URLs. Called during background sweep.
    /// Returns number of rows deleted.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct);
}
```

### `IMarketplaceService`

**File:** `src/MSOSync.Plugin/Marketplace/IMarketplaceService.cs`

```csharp
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Fetches plugin catalog data from the configured remote registry.
/// Applies two-tier caching: IMemoryCache (short-term) then DB cache (mid-term).
/// Implemented in MSOSync.Metadata (or the 2C infrastructure project).
/// Registered as Scoped.
/// </summary>
public interface IMarketplaceService
{
    /// <summary>
    /// Search the registry catalog. Applies category and text filters.
    /// Uses the DB cache if results are not expired. Falls back to remote on cache miss.
    /// </summary>
    /// <param name="query">Optional free-text search term (plugin name, author, description).</param>
    /// <param name="category">Optional category filter (exact match, case-insensitive).</param>
    /// <param name="page">1-based page number. Default: 1.</param>
    /// <param name="pageSize">Results per page. Min: 1. Max: 100. Default: 20.</param>
    Task<RegistrySearchResult> SearchAsync(
        string? query,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Fetches full plugin details including all version history.
    /// Returns null when the plugin ID is not found in the registry.
    /// </summary>
    Task<RegistryPluginEntry?> GetPluginAsync(string pluginId, CancellationToken ct);

    /// <summary>
    /// Returns all versions for the given plugin ID.
    /// Returns empty list when plugin is not found.
    /// </summary>
    Task<IReadOnlyList<RegistryVersionEntry>> GetVersionsAsync(string pluginId, CancellationToken ct);

    /// <summary>
    /// Checks whether a newer version is available in the registry for the installed version.
    /// Returns null when plugin is not in the registry or installed version is already latest.
    /// </summary>
    Task<RegistryVersionEntry?> GetLatestUpdateAsync(
        string pluginId,
        string installedVersion,
        CancellationToken ct);
}
```

### `IPluginUpdateService`

**File:** `src/MSOSync.Plugin/Marketplace/IPluginUpdateService.cs`

```csharp
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Compares locally installed plugin versions against the remote registry.
/// Registered as Scoped.
/// </summary>
public interface IPluginUpdateService
{
    /// <summary>
    /// Checks a single installed plugin for available updates.
    /// Returns null when the plugin is not in the registry or is already at the latest version.
    /// </summary>
    Task<PluginUpdateManifest?> CheckAsync(
        string pluginId,
        string installedVersion,
        CancellationToken ct);

    /// <summary>
    /// Checks all currently installed plugins for updates.
    /// Iterates IPluginStore.GetAllAsync and calls CheckAsync for each.
    /// Does NOT use Task.WhenAll — calls are sequential to avoid saturating the remote.
    /// Plugins not found in the registry are silently skipped (not an error).
    /// </summary>
    Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct);
}
```

**File:** `src/MSOSync.Plugin/Marketplace/Models/PluginUpdateManifest.cs`

```csharp
namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>
/// Describes an available update for an installed plugin.
/// </summary>
public sealed record PluginUpdateManifest(
    string PluginId,
    string InstalledVersion,
    string AvailableVersion,
    string DownloadUrl,
    string Sha256,
    string? ReleaseNotes,
    DateTime PublishedAt);
```

---

## API — DTOs

All DTOs live in `src/MSOSync.Api/Dtos/Marketplace/`.

### `MarketplaceSearchParams`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceSearchParams
{
    public string? Query    { get; init; }
    public string? Category { get; init; }
    public int     Page     { get; init; } = 1;
    public int     PageSize { get; init; } = 20;
}
```

**FluentValidation:** `MarketplaceSearchParamsValidator`

```csharp
using FluentValidation;

namespace MSOSync.Api.Dtos.Marketplace;

public sealed class MarketplaceSearchParamsValidator : AbstractValidator<MarketplaceSearchParams>
{
    public MarketplaceSearchParamsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Query).MaximumLength(200).When(x => x.Query != null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category != null);
    }
}
```

### `MarketplacePluginListItemDto`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplacePluginListItemDto(
    string   Id,
    string   Name,
    string   Author,
    string   Description,
    string   Category,
    IReadOnlyList<string> Tags,
    string   LatestVersion,
    string   MinHostVersion,
    long     DownloadCount,
    double   Rating,
    int      RatingCount,
    DateTime PublishedAt,
    DateTime UpdatedAt,
    string?  IconUrl,
    bool     Verified);
```

### `MarketplaceVersionDto`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceVersionDto(
    string   Version,
    string   MinHostVersion,
    string   MaxHostVersion,
    DateTime PublishedAt,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    bool     Deprecated);
```

### `MarketplacePluginDetailDto`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplacePluginDetailDto(
    string   Id,
    string   Name,
    string   Author,
    string   Description,
    string   Category,
    IReadOnlyList<string> Tags,
    string   LatestVersion,
    string   MinHostVersion,
    long     DownloadCount,
    double   Rating,
    int      RatingCount,
    DateTime PublishedAt,
    DateTime UpdatedAt,
    string?  IconUrl,
    string?  ProjectUrl,
    string?  LicenseId,
    bool     Verified,
    IReadOnlyList<MarketplaceVersionDto> Versions);
```

### `MarketplaceInstallRequest`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallRequest
{
    /// <summary>
    /// Specific version to install. When null, installs the latest version.
    /// </summary>
    public string? Version { get; init; }
}
```

**FluentValidation:** `MarketplaceInstallRequestValidator`

```csharp
using FluentValidation;

namespace MSOSync.Api.Dtos.Marketplace;

public sealed class MarketplaceInstallRequestValidator : AbstractValidator<MarketplaceInstallRequest>
{
    public MarketplaceInstallRequestValidator()
    {
        RuleFor(x => x.Version)
            .Matches(@"^\d+\.\d+\.\d+$")
            .WithMessage("Version must be a valid semantic version (major.minor.patch).")
            .When(x => x.Version != null);
    }
}
```

### `MarketplaceInstallResult`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallResult(
    bool    Success,
    string  PluginId,
    string  InstalledVersion,
    bool    RestartRequired,
    string? ErrorMessage);
```

### `MarketplaceUpdateManifestDto`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceUpdateManifestDto(
    string   PluginId,
    string   InstalledVersion,
    string   AvailableVersion,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    DateTime PublishedAt);
```

### `BulkUpdateCheckRequest`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

/// <summary>Request body for POST /api/v1/marketplace/updates/check.</summary>
public sealed record BulkUpdateCheckRequest
{
    /// <summary>
    /// When true, only returns plugins that HAVE available updates.
    /// When false (default), returns all checked plugins including up-to-date ones.
    /// </summary>
    public bool UpdatesOnly { get; init; } = false;
}
```

**FluentValidation:** `BulkUpdateCheckRequestValidator` — no field constraints needed; class is self-validating by type. Register a no-op validator or omit entirely (no error possible).

### `BulkUpdateCheckResult`

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record BulkUpdateCheckResult(
    int TotalChecked,
    int UpdatesAvailable,
    IReadOnlyList<MarketplaceUpdateManifestDto> Updates);
```

---

## API — `MarketplaceController`

**File:** `src/MSOSync.Api/Controllers/MarketplaceController.cs`

**Base route:** `api/v1/marketplace`
**Authorization:** `[Authorize(Policy = "AdminOnly")]` on the class.

All endpoints check `MarketplaceOptions.IsConfigured` first and return `503 Service Unavailable` with a clear message when the registry is unconfigured. This guard is centralized in a private helper (`EnsureConfigured()`) to avoid repetition.

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Plugin.Marketplace;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Policy = "AdminOnly")]
public sealed class MarketplaceController(
    IMarketplaceService            marketplaceService,
    IPluginUpdateService           updateService,
    IOptions<MarketplaceOptions>   options,
    IValidator<MarketplaceSearchParams>    searchValidator,
    IValidator<MarketplaceInstallRequest>  installValidator,
    IValidator<BulkUpdateCheckRequest>     bulkValidator) : ControllerBase
{
    private MarketplaceOptions Opts => options.Value;

    // ── Search / List ──────────────────────────────────────────────────────────

    [HttpGet("plugins")]
    [ProducesResponseType(typeof(PagedResponse<MarketplacePluginListItemDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> Search(
        [FromQuery] MarketplaceSearchParams @params,
        CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var validation = await searchValidator.ValidateAsync(@params, ct);
        if (!validation.IsValid) return ValidationProblem(validation.ToValidationProblemDetails());

        var result = await marketplaceService.SearchAsync(
            @params.Query, @params.Category, @params.Page, @params.PageSize, ct);

        var dtos = result.Data.Select(MapToListItem).ToList();
        return Ok(new PagedResponse<MarketplacePluginListItemDto>(
            dtos, result.Total, result.Page, result.PageSize, result.TotalPages));
    }

    // ── Plugin Detail ──────────────────────────────────────────────────────────

    [HttpGet("plugins/{id}")]
    [ProducesResponseType(typeof(MarketplacePluginDetailDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> GetPlugin(string id, CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var entry = await marketplaceService.GetPluginAsync(id, ct);
        if (entry is null) return NotFound();
        return Ok(MapToDetail(entry));
    }

    // ── Version History ────────────────────────────────────────────────────────

    [HttpGet("plugins/{id}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<MarketplaceVersionDto>), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> GetVersions(string id, CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var versions = await marketplaceService.GetVersionsAsync(id, ct);
        if (versions.Count == 0) return NotFound();
        return Ok(versions.Select(MapVersion).ToList());
    }

    // ── Install ────────────────────────────────────────────────────────────────

    [HttpPost("plugins/{id}/install")]
    [ProducesResponseType(typeof(MarketplaceInstallResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> Install(
        string id,
        [FromBody] MarketplaceInstallRequest request,
        CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var validation = await installValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.ToValidationProblemDetails());

        var entry = await marketplaceService.GetPluginAsync(id, ct);
        if (entry is null) return NotFound();

        var versionEntry = request.Version is not null
            ? entry.Versions.FirstOrDefault(v => v.Version == request.Version)
            : entry.Versions.FirstOrDefault(v => v.Version == entry.LatestVersion);

        if (versionEntry is null)
            return NotFound();

        // Delegate to IPluginInstaller (from Phase 2C.1).
        // IPluginInstaller.InstallFromUrlAsync downloads the .msopkg, verifies SHA-256,
        // extracts, validates manifest, copies to plugins/ directory, and upserts SyncPlugin.
        var installer = HttpContext.RequestServices.GetRequiredService<IPluginInstaller>();
        var result = await installer.InstallFromUrlAsync(
            id, versionEntry.Version, versionEntry.DownloadUrl, versionEntry.Sha256, ct);

        return Ok(new MarketplaceInstallResult(
            result.Success,
            id,
            versionEntry.Version,
            RestartRequired: true,
            result.ErrorMessage));
    }

    // ── Single Plugin Update Check ────────────────────────────────────────────

    [HttpGet("plugins/{id}/updates")]
    [ProducesResponseType(typeof(MarketplaceUpdateManifestDto), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> CheckUpdate(string id, CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        // Resolve current installed version from IPluginRegistry
        var registry = HttpContext.RequestServices.GetRequiredService<IPluginRegistry>();
        var descriptor = registry.GetById(id);
        if (descriptor is null) return NotFound();

        var manifest = await updateService.CheckAsync(id, descriptor.Version, ct);
        if (manifest is null) return NoContent();   // no update available
        return Ok(MapUpdateManifest(manifest));
    }

    // ── Bulk Update Check ─────────────────────────────────────────────────────

    [HttpPost("updates/check")]
    [ProducesResponseType(typeof(BulkUpdateCheckResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> BulkCheckUpdates(
        [FromBody] BulkUpdateCheckRequest request,
        CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var validation = await bulkValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.ToValidationProblemDetails());

        var manifests = await updateService.CheckAllAsync(ct);

        var dtos = request.UpdatesOnly
            ? manifests.Select(MapUpdateManifest).ToList()
            : manifests.Select(MapUpdateManifest).ToList();   // same path; filter applied in service

        return Ok(new BulkUpdateCheckResult(
            TotalChecked: manifests.Count,
            UpdatesAvailable: manifests.Count,
            Updates: dtos));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ObjectResult ServiceUnavailable() =>
        StatusCode(503, new ErrorResponse(
            "Marketplace is not configured. Set Marketplace:RegistryUrl in appsettings.json."));

    private static MarketplacePluginListItemDto MapToListItem(
        MSOSync.Plugin.Marketplace.Models.RegistryPluginEntry e) => new(
        e.Id, e.Name, e.Author, e.Description, e.Category, e.Tags,
        e.LatestVersion, e.MinHostVersion, e.DownloadCount, e.Rating,
        e.RatingCount, e.PublishedAt, e.UpdatedAt, e.IconUrl, e.Verified);

    private static MarketplacePluginDetailDto MapToDetail(
        MSOSync.Plugin.Marketplace.Models.RegistryPluginEntry e) => new(
        e.Id, e.Name, e.Author, e.Description, e.Category, e.Tags,
        e.LatestVersion, e.MinHostVersion, e.DownloadCount, e.Rating,
        e.RatingCount, e.PublishedAt, e.UpdatedAt, e.IconUrl,
        e.ProjectUrl, e.LicenseId, e.Verified,
        e.Versions.Select(MapVersion).ToList());

    private static MarketplaceVersionDto MapVersion(
        MSOSync.Plugin.Marketplace.Models.RegistryVersionEntry v) => new(
        v.Version, v.MinHostVersion, v.MaxHostVersion,
        v.PublishedAt, v.DownloadUrl, v.Sha256, v.ReleaseNotes, v.Deprecated);

    private static MarketplaceUpdateManifestDto MapUpdateManifest(
        MSOSync.Plugin.Marketplace.Models.PluginUpdateManifest m) => new(
        m.PluginId, m.InstalledVersion, m.AvailableVersion,
        m.DownloadUrl, m.Sha256, m.ReleaseNotes, m.PublishedAt);
}
```

> **Note on `IPluginInstaller`:** Defined in Phase 2C.1. The controller resolves it via `HttpContext.RequestServices` because it is not injected at construction time — this avoids forcing a compile-time dependency on `MSOSync.Plugin` (which does not know about the installer in 2C.1's project). If 2C.1 places `IPluginInstaller` in `MSOSync.Plugin`, inject it through the constructor instead.

---

## Service Implementations

### `MarketplaceService`

**File:** `src/MSOSync.Metadata/Marketplace/MarketplaceService.cs` (or equivalent 2C infra project)

**Registration:** Scoped

**Caching strategy:**

1. **Memory cache (L1):** `IMemoryCache` key = `marketplace:search:{cacheKey}` or `marketplace:plugin:{pluginId}`. TTL = `MarketplaceOptions.MemoryCacheMinutes`. Checked first on every request.
2. **DB cache (L2):** `SyncMarketplaceCache` table via `IMarketplaceCacheStore`. TTL = `MarketplaceOptions.CacheMinutes`. Checked on memory cache miss. Provides persistence across process restarts.
3. **Remote fetch (L3):** HTTP call via named `HttpClient` (`"MarketplaceRegistry"`) with Polly retry. Triggered on DB cache miss or expiry. Writes result to both L2 and L1.

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace MSOSync.Metadata.Marketplace;

public sealed class MarketplaceService(
    IHttpClientFactory           httpClientFactory,
    IMarketplaceCacheStore       cacheStore,
    IMemoryCache                 memoryCache,
    IOptions<MarketplaceOptions> options,
    ILogger<MarketplaceService>  logger) : IMarketplaceService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private MarketplaceOptions Opts => options.Value;

    public async Task<RegistrySearchResult> SearchAsync(
        string? query, string? category, int page, int pageSize, CancellationToken ct)
    {
        var cacheKey = BuildSearchCacheKey(query, category, page, pageSize);
        var memKey   = $"marketplace:search:{cacheKey}";

        // L1: memory
        if (memoryCache.TryGetValue(memKey, out RegistrySearchResult? cached) && cached is not null)
            return cached;

        // L2: DB (returns null on miss/expiry)
        var dbEntries = await cacheStore.GetSearchCacheAsync(Opts.RegistryUrl!, cacheKey, ct);
        if (dbEntries is not null)
        {
            // Reconstruct paged result from cached flat list
            var result = BuildPagedResult(dbEntries, page, pageSize);
            memoryCache.Set(memKey, result, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return result;
        }

        // L3: remote
        return await FetchSearchAsync(query, category, page, pageSize, memKey, ct);
    }

    public async Task<RegistryPluginEntry?> GetPluginAsync(string pluginId, CancellationToken ct)
    {
        var memKey = $"marketplace:plugin:{pluginId}";

        if (memoryCache.TryGetValue(memKey, out RegistryPluginEntry? cached) && cached is not null)
            return cached;

        var dbEntry = await cacheStore.GetPluginCacheAsync(Opts.RegistryUrl!, pluginId, ct);
        if (dbEntry is not null)
        {
            memoryCache.Set(memKey, dbEntry, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return dbEntry;
        }

        return await FetchPluginAsync(pluginId, memKey, ct);
    }

    public async Task<IReadOnlyList<RegistryVersionEntry>> GetVersionsAsync(
        string pluginId, CancellationToken ct)
    {
        var entry = await GetPluginAsync(pluginId, ct);
        return entry?.Versions ?? [];
    }

    public async Task<RegistryVersionEntry?> GetLatestUpdateAsync(
        string pluginId, string installedVersion, CancellationToken ct)
    {
        var entry = await GetPluginAsync(pluginId, ct);
        if (entry is null) return null;
        if (!IsNewer(entry.LatestVersion, installedVersion)) return null;
        return entry.Versions.FirstOrDefault(v => v.Version == entry.LatestVersion);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<RegistrySearchResult> FetchSearchAsync(
        string? query, string? category, int page, int pageSize,
        string memKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MarketplaceRegistry");
        var url    = BuildSearchUrl(query, category, page, pageSize);

        try
        {
            var response = await client.GetFromJsonAsync<RegistrySearchResult>(url, JsonOpts, ct)
                           ?? new RegistrySearchResult();

            // Write to DB cache (sequential — no Task.WhenAll on shared DbContext)
            if (Opts.CacheMinutes > 0)
                await cacheStore.UpsertBulkAsync(
                    Opts.RegistryUrl!, response.Data, Opts.CacheMinutes, ct);

            memoryCache.Set(memKey, response, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Marketplace registry search failed: {Url}", url);
            return new RegistrySearchResult();
        }
    }

    private async Task<RegistryPluginEntry?> FetchPluginAsync(
        string pluginId, string memKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MarketplaceRegistry");
        var url    = $"plugins/{Uri.EscapeDataString(pluginId)}";

        try
        {
            var entry = await client.GetFromJsonAsync<RegistryPluginEntry>(url, JsonOpts, ct);
            if (entry is null) return null;

            if (Opts.CacheMinutes > 0)
                await cacheStore.UpsertAsync(Opts.RegistryUrl!, entry, Opts.CacheMinutes, ct);

            memoryCache.Set(memKey, entry, TimeSpan.FromMinutes(Opts.MemoryCacheMinutes));
            return entry;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Marketplace registry fetch failed for plugin: {Id}", pluginId);
            return null;
        }
    }

    private string BuildSearchUrl(string? query, string? category, int page, int pageSize)
    {
        var q = $"plugins?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query))
            q += $"&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(category))
            q += $"&category={Uri.EscapeDataString(category)}";
        return q;
    }

    private static string BuildSearchCacheKey(
        string? query, string? category, int page, int pageSize) =>
        $"{query}|{category}|{page}|{pageSize}".ToLowerInvariant();

    private static RegistrySearchResult BuildPagedResult(
        IReadOnlyList<RegistryPluginEntry> entries, int page, int pageSize)
    {
        var total      = entries.Count;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var data       = entries.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new RegistrySearchResult
        {
            Data       = data,
            Total      = total,
            Page       = page,
            PageSize   = pageSize,
            TotalPages = totalPages
        };
    }

    /// <summary>Returns true if candidateVersion is strictly greater than baseVersion.</summary>
    private static bool IsNewer(string candidateVersion, string baseVersion)
    {
        if (!Version.TryParse(candidateVersion, out var candidate)) return false;
        if (!Version.TryParse(baseVersion,      out var @base))     return false;
        return candidate > @base;
    }
}
```

### `PluginUpdateService`

**File:** `src/MSOSync.Metadata/Marketplace/PluginUpdateService.cs`

**Registration:** Scoped

```csharp
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Metadata.Marketplace;

public sealed class PluginUpdateService(
    IMarketplaceService marketplaceService,
    IPluginStore        pluginStore) : IPluginUpdateService
{
    public async Task<PluginUpdateManifest?> CheckAsync(
        string pluginId, string installedVersion, CancellationToken ct)
    {
        var latestEntry = await marketplaceService.GetLatestUpdateAsync(
            pluginId, installedVersion, ct);

        if (latestEntry is null) return null;

        return new PluginUpdateManifest(
            pluginId,
            installedVersion,
            latestEntry.Version,
            latestEntry.DownloadUrl,
            latestEntry.Sha256,
            latestEntry.ReleaseNotes,
            latestEntry.PublishedAt);
    }

    public async Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct)
    {
        var installed = await pluginStore.GetAllAsync(ct);
        var results   = new List<PluginUpdateManifest>(installed.Count);

        // Sequential — no Task.WhenAll on shared DbContext or shared HTTP client burst
        foreach (var record in installed)
        {
            var manifest = await CheckAsync(record.PluginId, record.PluginVersion, ct);
            if (manifest is not null)
                results.Add(manifest);
        }

        return results;
    }
}
```

### `MarketplaceCacheStore`

**File:** `src/MSOSync.Persistence/Stores/MarketplaceCacheStore.cs`

**Registration:** Scoped

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using System.Text.Json;

namespace MSOSync.Persistence.Stores;

public sealed class MarketplaceCacheStore(AppDbContext db) : IMarketplaceCacheStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private static string Normalize(string url) => url.TrimEnd('/');

    public async Task<IReadOnlyList<RegistryPluginEntry>?> GetSearchCacheAsync(
        string registryUrl, string cacheKey, CancellationToken ct)
    {
        var url  = Normalize(registryUrl);
        var now  = DateTime.UtcNow;

        var rows = await db.MarketplaceCache
            .AsNoTracking()
            .Where(r => r.RegistryUrl == url && r.ExpiresAt > now)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        return rows
            .Select(r => JsonSerializer.Deserialize<RegistryPluginEntry>(r.MetadataJson, JsonOpts))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    public async Task<RegistryPluginEntry?> GetPluginCacheAsync(
        string registryUrl, string pluginId, CancellationToken ct)
    {
        var url = Normalize(registryUrl);
        var now = DateTime.UtcNow;

        var row = await db.MarketplaceCache
            .AsNoTracking()
            .Where(r => r.RegistryUrl == url && r.PluginId == pluginId && r.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return JsonSerializer.Deserialize<RegistryPluginEntry>(row.MetadataJson, JsonOpts);
    }

    public async Task UpsertAsync(
        string registryUrl, RegistryPluginEntry entry, int cacheMinutes, CancellationToken ct)
    {
        var url  = Normalize(registryUrl);
        var now  = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(entry, JsonOpts);

        var existing = await db.MarketplaceCache
            .Where(r => r.RegistryUrl == url && r.PluginId == entry.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            db.MarketplaceCache.Add(new SyncMarketplaceCache
            {
                RegistryUrl   = url,
                PluginId      = entry.Id,
                LatestVersion = entry.LatestVersion,
                MetadataJson  = json,
                CachedAt      = now,
                ExpiresAt     = now.AddMinutes(cacheMinutes)
            });
        }
        else
        {
            existing.LatestVersion = entry.LatestVersion;
            existing.MetadataJson  = json;
            existing.CachedAt      = now;
            existing.ExpiresAt     = now.AddMinutes(cacheMinutes);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBulkAsync(
        string registryUrl, IReadOnlyList<RegistryPluginEntry> entries,
        int cacheMinutes, CancellationToken ct)
    {
        // Sequential — must not use Task.WhenAll on a shared DbContext instance
        foreach (var entry in entries)
            await UpsertAsync(registryUrl, entry, cacheMinutes, ct);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var expired = await db.MarketplaceCache
            .Where(r => r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        db.MarketplaceCache.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
```

---

## HTTP Client Registration (Polly + `IHttpClientFactory`)

**File:** `src/MSOSync.App/ServiceCollectionExtensions.cs` (or `Program.cs`)

```csharp
services.AddHttpClient("MarketplaceRegistry", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<MarketplaceOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.RegistryUrl))
        client.BaseAddress = new Uri(opts.RegistryUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(opts.HttpTimeoutSeconds);
    if (!string.IsNullOrWhiteSpace(opts.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", $"MSOSync/{HostVersion.Current}");
})
.AddStandardResilienceHandler(opts =>
{
    // Polly v8 / Microsoft.Extensions.Http.Resilience standard pipeline:
    // - Rate limiter
    // - Total request timeout
    // - Retry (exponential back-off)
    // - Circuit breaker
    // - Attempt timeout
    // Customize retry count from MarketplaceOptions
});
```

If `Microsoft.Extensions.Http.Resilience` is not yet in the solution, use `.AddTransientHttpErrorPolicy` from `Polly.Extensions.Http`:

```csharp
.AddTransientHttpErrorPolicy(p =>
    p.WaitAndRetryAsync(
        retryCount: opts.RetryCount,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

---

## DI Registration — `AddMarketplace`

**File:** `src/MSOSync.App/MarketplaceServiceExtensions.cs`

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Metadata.Marketplace;
using MSOSync.Persistence.Stores;
using MSOSync.Plugin.Marketplace;

namespace MSOSync.App;

public static class MarketplaceServiceExtensions
{
    public static IServiceCollection AddMarketplace(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MarketplaceOptions>(
            configuration.GetSection(MarketplaceOptions.SectionName));

        // Validators
        services.AddScoped<IValidator<MarketplaceSearchParams>,
            MarketplaceSearchParamsValidator>();
        services.AddScoped<IValidator<MarketplaceInstallRequest>,
            MarketplaceInstallRequestValidator>();
        services.AddScoped<IValidator<BulkUpdateCheckRequest>,
            BulkUpdateCheckRequestValidator>();

        // Cache store (Scoped — shares DbContext lifetime)
        services.AddScoped<IMarketplaceCacheStore, MarketplaceCacheStore>();

        // Services (Scoped)
        services.AddScoped<IMarketplaceService, MarketplaceService>();
        services.AddScoped<IPluginUpdateService, PluginUpdateService>();

        return services;
    }
}
```

Call from `Program.cs`:

```csharp
builder.Services.AddMarketplace(builder.Configuration);
```

---

## Caching Strategy

### Two-Tier Cache

| Tier | Implementation | TTL Source | Key Pattern | Scope |
|---|---|---|---|---|
| L1 Memory | `IMemoryCache` | `MemoryCacheMinutes` | `marketplace:search:{cacheKey}` / `marketplace:plugin:{id}` | Per-process, lost on restart |
| L2 DB | `SyncMarketplaceCache` | `CacheMinutes` | `(RegistryUrl, PluginId)` index | Persistent across restarts |

### Cache Miss Path

```
Request arrives
  → Check L1 (IMemoryCache)
    Hit → Return immediately
    Miss → Check L2 (DB, AsNoTracking, ExpiresAt > now)
      Hit → Populate L1, return
      Miss (or expired row) → Fetch from remote registry
        Success → Write L2 (upsert), write L1, return
        Failure → Log warning, return empty result or null (no throw)
```

### Cache Invalidation

There is no active invalidation. Expiry is passive: `ExpiresAt` column controls staleness. `MarketplaceCacheStore.PurgeExpiredAsync` can be called from a lightweight background job (e.g., a `BackgroundService` that runs once every `CacheMinutes` interval). This job is optional — expired rows are filtered in `WHERE ExpiresAt > @now` on every read, so stale rows never surface even without purging.

### Search Cache Key

The search cache key for DB lookup is constructed as `{query}|{category}|{page}|{pageSize}` (lowercased). This is stored as a stable identifier to distinguish different search parameter sets. The DB cache table does not store a `cacheKey` column — instead, on a search cache hit the service loads **all non-expired entries** for the registry URL and rebuilds the paged result in memory. This is simpler than per-query keying in the DB and stays correct as long as the total catalog is modest (< 10,000 entries). Large catalogs require a dedicated column for search cache differentiation — deferred to 2C.3.

---

## Error Handling

| Condition | Response |
|---|---|
| `MarketplaceOptions.IsConfigured == false` | 503 `{"error": "Marketplace is not configured. Set Marketplace:RegistryUrl in appsettings.json."}` |
| FluentValidation failure | 400 `ValidationProblemDetails` (standard ASP.NET Core shape) |
| Plugin not found in registry | 404 (no body) |
| Specific version not found | 404 (no body) |
| Remote registry HTTP error (transient) | Polly retries; on final failure → `IMarketplaceService` returns empty result / null; controller returns 200 with empty data or 404 |
| Remote registry 404 | `MarketplaceService.FetchPluginAsync` catches `HttpRequestException` with `StatusCode == 404` → returns null → controller returns 404 |
| Remote registry 5xx after all retries | Returns null / empty; controller returns 503 with message "Registry temporarily unavailable" |
| EF concurrency (upsert race) | `UpsertAsync` reads existing before writing; two concurrent writes both complete — last writer wins, which is acceptable for a cache |
| `IPluginInstaller` failure | `MarketplaceInstallResult.Success == false` with `ErrorMessage` set; HTTP 200 (the call succeeded, the install failed — client inspects result body) |

All exceptions from `MarketplaceService` are caught internally and logged with `LogWarning`. They do not propagate to the controller. This prevents a remote registry outage from returning 500 — degraded-but-functional is preferred.

---

## Migration — M035

**File:** `src/MSOSync.Persistence/Migrations/M035_MarketplaceCache.cs`

Creates `msosync.sync_marketplace_cache` with:
- `id` int identity PK
- `registry_url` nvarchar(500) not null
- `plugin_id` nvarchar(200) not null
- `latest_version` nvarchar(50) not null
- `metadata_json` nvarchar(max) not null
- `cached_at` datetime2 not null
- `expires_at` datetime2 not null
- Unique index `IX_sync_marketplace_cache_registry_plugin` on `(registry_url, plugin_id)`
- Index `IX_sync_marketplace_cache_expires_at` on `expires_at`

Table count goes from 44 → 45.

Update the schema count assertion in `PersistenceTests` from `SchemaCreated_All44TablesExist` → `SchemaCreated_All45TablesExist`.

---

## Logging Event IDs

| ID | Event | Level |
|---|---|---|
| `Marketplace2001` | Remote registry search fetched (page, total, elapsed) | Information |
| `Marketplace2002` | Remote registry plugin detail fetched (pluginId, elapsed) | Information |
| `Marketplace2003` | Remote registry search failed (exception) | Warning |
| `Marketplace2004` | Remote registry plugin fetch failed (pluginId, exception) | Warning |
| `Marketplace2005` | DB cache write completed (pluginId, expiresAt) | Debug |
| `Marketplace2006` | DB cache miss — fetching from remote (pluginId) | Debug |
| `Marketplace2007` | Install triggered from marketplace (pluginId, version) | Information |
| `Marketplace2008` | Bulk update check completed (totalChecked, updatesFound) | Information |
| `Marketplace2009` | Expired cache entries purged (count) | Debug |

All event IDs registered as `EventId` constants in a `MarketplaceLogEvents` static class in `MSOSync.Plugin.Marketplace`.

---

## Testing Approach

### Unit Tests — `tests/MSOSync.PluginTests/Marketplace/`

| Test Class | Coverage |
|---|---|
| `MarketplaceOptionsTests` | `IsConfigured` returns false when RegistryUrl is null, empty, or whitespace; true when non-empty |
| `MarketplaceServiceCacheTests` | L1 hit skips L2 and remote; L2 hit skips remote; L2 miss triggers remote; remote failure returns empty; `IsNewer` returns correct results for equal, lesser, greater versions |
| `PluginUpdateServiceTests` | `CheckAsync` returns null when versions are equal; returns manifest when registry has newer; `CheckAllAsync` iterates all installed and aggregates; skips plugins not in registry |
| `MarketplaceSearchParamsValidatorTests` | Page < 1 → invalid; PageSize 0 → invalid; PageSize 101 → invalid; Query > 200 chars → invalid; valid params → valid |
| `MarketplaceInstallRequestValidatorTests` | Non-semver version string → invalid; null version → valid (latest); valid semver → valid |
| `MarketplaceCacheStoreTests` | Expired row not returned; non-expired row returned; upsert creates new row; upsert updates existing row; `PurgeExpiredAsync` deletes only expired rows |

### Integration Tests — `tests/MSOSync.IntegrationTests/Marketplace/`

| Test | Scenario |
|---|---|
| `Search_WithoutRegistryUrl_Returns503` | No RegistryUrl configured → GET /api/v1/marketplace/plugins → 503 |
| `Search_WithRegistryUrl_ReturnsPagedResult` | Stub registry returns 3 entries → response paged correctly; DB cache row created |
| `GetPlugin_NotInRegistry_Returns404` | Stub returns 404 → GET /api/v1/marketplace/plugins/missing → 404 |
| `GetPlugin_CacheHit_NoRemoteCall` | DB cache row with future ExpiresAt exists → no HTTP call to stub registry |
| `GetVersions_ReturnsAllVersions` | Entry with 3 versions → all 3 returned |
| `Install_LatestVersion_DelegatesToInstaller` | POST /install with no version body → installer called with LatestVersion |
| `Install_SpecificVersion_DelegatesToInstaller` | POST /install with "1.2.0" → installer called with "1.2.0" |
| `Install_UnknownVersion_Returns404` | Requested version not in entry.Versions → 404 |
| `CheckUpdate_NoUpdateAvailable_Returns204` | Installed version == latest → 204 No Content |
| `CheckUpdate_UpdateAvailable_ReturnsManifest` | Installed "1.0.0", registry latest "1.1.0" → manifest returned with correct fields |
| `BulkCheckUpdates_MixedUpdates_ReturnsCorrectCount` | 3 installed, 1 has update → TotalChecked=3, UpdatesAvailable=1 |
| `BulkCheckUpdates_EmptyInstalled_ReturnsZero` | No installed plugins → TotalChecked=0, UpdatesAvailable=0 |
| `PurgeExpired_RemovesOnlyStaleRows` | 2 expired + 1 valid rows → 2 deleted, 1 remains |

**Stub registry pattern:** Integration tests use `WireMock.Net` (or `Microsoft.AspNetCore.TestHost` with a stub controller) to serve fake registry JSON without network calls.

---

## Global Constraints

The following constraints apply to every file produced for this phase. Violating any of them is a build-blocking defect.

| Constraint | Detail |
|---|---|
| Marketplace optional | When `MarketplaceOptions.IsConfigured == false`, every controller action returns 503 before touching any service. The service layer is never called. |
| Authorization | `[Authorize(Policy = "AdminOnly")]` on the controller class. No endpoint is reachable without authentication. |
| `ProducesResponseType` | Every public controller action declares all possible status codes with `[ProducesResponseType(...)]`. |
| FluentValidation | `MarketplaceSearchParams` and `MarketplaceInstallRequest` and `BulkUpdateCheckRequest` are validated via injected `IValidator<T>` before any service call. |
| `AsNoTracking()` | All EF read queries in `MarketplaceCacheStore` use `AsNoTracking()`. No tracked reads. |
| No `Task.WhenAll` on shared `DbContext` | `UpsertBulkAsync` and `CheckAllAsync` iterate sequentially. Each `await` completes before the next begins. |
| No lazy loading | No `Include()` unless explicitly required by a query (none needed in this module). |
| Migration required | `M035_MarketplaceCache` must be added. The schema count assertion in persistence tests must be updated. |
| `AppDbContext` | `DbSet<SyncMarketplaceCache> MarketplaceCache` must be added to `AppDbContext`. |
| `[GlobalEntity]` attribute | `SyncMarketplaceCache` must carry `[GlobalEntity]` (same pattern as `SyncPlugin`) so tenant query filters do not apply to it. |
| One-EF-context-per-scope | `MarketplaceCacheStore` shares the request-scoped `AppDbContext`. It does not create a new context. |
| HTTP error resilience | `MarketplaceService` never throws to callers. Remote failures are caught, logged at Warning, and return graceful empty/null values. |
| Version comparison | Version comparisons use `System.Version.TryParse` (not string comparison). |

---

## Out of Scope (Deferred)

| Feature | Phase |
|---|---|
| Marketplace UI (React components: search page, install flow, update badge) | 2C.3 |
| Plugin signing verification on install | 2C.4 |
| Plugin sandbox (permission enforcement) | 2C.5 |
| Publisher portal / plugin submission workflow | 2C.6 |
| Rating submission API (`POST /marketplace/plugins/{id}/rate`) | 2C.6 |
| Webhook notifications on new plugin versions | 2C.6 |
| Auto-update background job (scheduled check + silent install) | 2C.7 |
| Multiple registry sources | 2D+ |
| Offline / air-gapped registry (local mirror) | Enterprise |
| CLI tooling (`msosync plugin install`) | 2H |
