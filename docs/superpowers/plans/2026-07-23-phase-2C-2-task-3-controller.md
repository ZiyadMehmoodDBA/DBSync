# Task 3 — Controller + DTOs

**Plan:** `2026-07-23-phase-2C-2-master.md`
**Scope:** All Marketplace DTOs + FluentValidation validators, `MarketplaceController` (6 endpoints), `appsettings.json` Marketplace section.

---

## Step 3.1 — DTOs

Create all files under `src/MSOSync.Api/Dtos/Marketplace/`.

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceSearchParams.cs`:

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

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplacePluginListItemDto.cs`:

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

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceVersionDto.cs`:

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

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplacePluginDetailDto.cs`:

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

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceInstallRequest.cs`:

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallRequest
{
    /// <summary>Specific version to install. When null, installs the latest version.</summary>
    public string? Version { get; init; }
}
```

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceInstallResult.cs`:

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallResult(
    bool    Success,
    string  PluginId,
    string  InstalledVersion,
    bool    RestartRequired,
    string? ErrorMessage);
```

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceUpdateManifestDto.cs`:

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

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/BulkUpdateCheckRequest.cs`:

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

/// <summary>Request body for POST /api/v1/marketplace/updates/check.</summary>
public sealed record BulkUpdateCheckRequest
{
    /// <summary>
    /// When true, only returns plugins that HAVE available updates.
    /// When false (default), the service includes all checked plugins in TotalChecked.
    /// </summary>
    public bool UpdatesOnly { get; init; } = false;
}
```

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/BulkUpdateCheckResult.cs`:

```csharp
namespace MSOSync.Api.Dtos.Marketplace;

public sealed record BulkUpdateCheckResult(
    int TotalChecked,
    int UpdatesAvailable,
    IReadOnlyList<MarketplaceUpdateManifestDto> Updates);
```

---

## Step 3.2 — FluentValidation validators

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceSearchParamsValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Api.Dtos.Marketplace;

public sealed class MarketplaceSearchParamsValidator : AbstractValidator<MarketplaceSearchParams>
{
    public MarketplaceSearchParamsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Query).MaximumLength(200).When(x => x.Query is not null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
    }
}
```

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/MarketplaceInstallRequestValidator.cs`:

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
            .When(x => x.Version is not null);
    }
}
```

- [ ] Create `src/MSOSync.Api/Dtos/Marketplace/BulkUpdateCheckRequestValidator.cs`:

```csharp
using FluentValidation;

namespace MSOSync.Api.Dtos.Marketplace;

/// <summary>No-op validator — BulkUpdateCheckRequest has no field constraints.</summary>
public sealed class BulkUpdateCheckRequestValidator : AbstractValidator<BulkUpdateCheckRequest>
{
    public BulkUpdateCheckRequestValidator() { }
}
```

---

## Step 3.3 — `MarketplaceController`

- [ ] Create `src/MSOSync.Api/Controllers/MarketplaceController.cs`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Policy = "AdminOnly")]
public sealed class MarketplaceController(
    IMarketplaceService                    marketplaceService,
    IPluginUpdateService                   updateService,
    IOptions<MarketplaceOptions>           options,
    IValidator<MarketplaceSearchParams>    searchValidator,
    IValidator<MarketplaceInstallRequest>  installValidator,
    IValidator<BulkUpdateCheckRequest>     bulkValidator) : ControllerBase
{
    private MarketplaceOptions Opts => options.Value;

    // ── Search / List ──────────────────────────────────────────────────────────

    /// <summary>Search the remote plugin registry catalog.</summary>
    [HttpGet("plugins")]
    [ProducesResponseType(typeof(PagedResponse<MarketplacePluginListItemDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> Search(
        [FromQuery] MarketplaceSearchParams @params,
        CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        await searchValidator.ValidateAndThrowAsync(@params, ct);

        var result = await marketplaceService.SearchAsync(
            @params.Query, @params.Category, @params.Page, @params.PageSize, ct);

        var dtos = result.Data.Select(MapToListItem).ToList();
        return Ok(new PagedResponse<MarketplacePluginListItemDto>(
            dtos, result.Total, result.Page, result.PageSize, result.TotalPages));
    }

    // ── Plugin Detail ──────────────────────────────────────────────────────────

    /// <summary>Get full plugin details including all versions.</summary>
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

    /// <summary>Get all available versions for a plugin.</summary>
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

    /// <summary>
    /// Install a plugin from the marketplace. When Version is null, installs the latest.
    /// Delegates to IPluginInstaller (Phase 2C.1). Always returns 200 — inspect Success field.
    /// </summary>
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

        await installValidator.ValidateAndThrowAsync(request, ct);

        var entry = await marketplaceService.GetPluginAsync(id, ct);
        if (entry is null) return NotFound();

        var versionEntry = request.Version is not null
            ? entry.Versions.FirstOrDefault(v => v.Version == request.Version)
            : entry.Versions.FirstOrDefault(v => v.Version == entry.LatestVersion);

        if (versionEntry is null) return NotFound();

        // Resolve IPluginInstaller at call time — defined in Phase 2C.1.
        // If 2C.1 places IPluginInstaller in MSOSync.Plugin, inject via constructor instead.
        var installer = HttpContext.RequestServices
            .GetRequiredService<MSOSync.Plugin.Abstractions.IPluginInstaller>();

        var logger = HttpContext.RequestServices
            .GetRequiredService<ILogger<MarketplaceController>>();
        logger.Log(LogLevel.Information, MarketplaceLogEvents.InstallTriggered,
            "Install triggered from marketplace. PluginId: {PluginId}, Version: {Version}",
            id, versionEntry.Version);

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

    /// <summary>
    /// Check whether a newer version is available for an installed plugin.
    /// Returns 204 when no update is available.
    /// </summary>
    [HttpGet("plugins/{id}/updates")]
    [ProducesResponseType(typeof(MarketplaceUpdateManifestDto), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> CheckUpdate(string id, CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        var registry = HttpContext.RequestServices
            .GetRequiredService<MSOSync.Plugin.Abstractions.IPluginRegistry>();
        var descriptor = registry.GetById(id);
        if (descriptor is null) return NotFound();

        var manifest = await updateService.CheckAsync(id, descriptor.Version, ct);
        if (manifest is null) return NoContent();
        return Ok(MapUpdateManifest(manifest));
    }

    // ── Bulk Update Check ─────────────────────────────────────────────────────

    /// <summary>Check all installed plugins for available updates.</summary>
    [HttpPost("updates/check")]
    [ProducesResponseType(typeof(BulkUpdateCheckResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public async Task<IActionResult> BulkCheckUpdates(
        [FromBody] BulkUpdateCheckRequest request,
        CancellationToken ct)
    {
        if (!Opts.IsConfigured) return ServiceUnavailable();

        await bulkValidator.ValidateAndThrowAsync(request, ct);

        var manifests = await updateService.CheckAllAsync(ct);
        var dtos      = manifests.Select(MapUpdateManifest).ToList();

        return Ok(new BulkUpdateCheckResult(
            TotalChecked:      manifests.Count,
            UpdatesAvailable:  manifests.Count,
            Updates:           dtos));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ObjectResult ServiceUnavailable() =>
        StatusCode(503, new ErrorResponse(
            "Marketplace is not configured. Set Marketplace:RegistryUrl in appsettings.json."));

    private static MarketplacePluginListItemDto MapToListItem(RegistryPluginEntry e) => new(
        e.Id, e.Name, e.Author, e.Description, e.Category, e.Tags,
        e.LatestVersion, e.MinHostVersion, e.DownloadCount, e.Rating,
        e.RatingCount, e.PublishedAt, e.UpdatedAt, e.IconUrl, e.Verified);

    private static MarketplacePluginDetailDto MapToDetail(RegistryPluginEntry e) => new(
        e.Id, e.Name, e.Author, e.Description, e.Category, e.Tags,
        e.LatestVersion, e.MinHostVersion, e.DownloadCount, e.Rating,
        e.RatingCount, e.PublishedAt, e.UpdatedAt, e.IconUrl,
        e.ProjectUrl, e.LicenseId, e.Verified,
        e.Versions.Select(MapVersion).ToList());

    private static MarketplaceVersionDto MapVersion(RegistryVersionEntry v) => new(
        v.Version, v.MinHostVersion, v.MaxHostVersion,
        v.PublishedAt, v.DownloadUrl, v.Sha256, v.ReleaseNotes, v.Deprecated);

    private static MarketplaceUpdateManifestDto MapUpdateManifest(PluginUpdateManifest m) => new(
        m.PluginId, m.InstalledVersion, m.AvailableVersion,
        m.DownloadUrl, m.Sha256, m.ReleaseNotes, m.PublishedAt);
}
```

> Note on validation pattern: The project uses `ValidateAndThrowAsync` (not `ValidateAsync` + manual check). `GlobalExceptionHandler` catches `FluentValidation.ValidationException` and maps it to a 400 `ValidationProblemDetails`. Do not introduce `ToValidationProblemDetails()` — it does not exist in this codebase.

> Note on `IPluginInstaller`: Defined in Phase 2C.1. If 2C.1 places the interface in `MSOSync.Plugin.Abstractions`, import it directly in the constructor. The `HttpContext.RequestServices.GetRequiredService<>` pattern shown is a safe fallback if the interface is not yet available at plan-write time.

---

## Step 3.4 — Add Marketplace section to `appsettings.json`

- [ ] Open `src/MSOSync.App/appsettings.json`
- [ ] Add the following block before the closing `}`:

```json
,
  "Marketplace": {
    "RegistryUrl": "",
    "ApiKey": "",
    "CacheMinutes": 60,
    "MemoryCacheMinutes": 5,
    "HttpTimeoutSeconds": 30,
    "RetryCount": 3
  }
```

The full file after edit ends with the `Marketplace` block then `}`.

---

## Step 3.5 — Build check

- [ ] Run:

```powershell
dotnet build src/MSOSync.Api/MSOSync.Api.csproj --no-restore
```

Expected: 0 errors. All 9 DTO files and the controller must compile cleanly.
