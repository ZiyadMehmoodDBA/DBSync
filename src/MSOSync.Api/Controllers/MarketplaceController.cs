using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using MSOSync.Plugin.Packaging.Abstractions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Policy = "AdminOnly")]
public sealed class MarketplaceController(
    IMarketplaceService                     marketplaceService,
    IPluginUpdateService                    updateService,
    IPluginInstaller                        installer,
    IPluginRegistry                         pluginRegistry,
    IHttpClientFactory                      httpClientFactory,
    IOptions<MarketplaceOptions>            options,
    IValidator<MarketplaceSearchParams>     searchValidator,
    IValidator<MarketplaceInstallRequest>   installValidator,
    IValidator<BulkUpdateCheckRequest>      bulkValidator,
    ILogger<MarketplaceController>          logger) : ControllerBase
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
    /// Downloads the package archive then delegates to IPluginInstaller. Always returns 200
    /// — inspect the Success field for the outcome.
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

        logger.Log(LogLevel.Information, MarketplaceLogEvents.InstallTriggered,
            "Install triggered from marketplace. PluginId: {PluginId}, Version: {Version}",
            id, versionEntry.Version);

        // Download the package archive to a temp file, then install
        var tempPath = Path.Combine(Path.GetTempPath(), $"msopkg-{id}-{versionEntry.Version}-{Guid.NewGuid():N}.msopkg");
        try
        {
            var client = httpClientFactory.CreateClient("MarketplaceRegistry");
            using (var response = await client.GetAsync(versionEntry.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, ct);
            }

            var result = await installer.InstallAsync(tempPath, ct);

            // I10: Invalidate the marketplace cache for this plugin so subsequent reads
            // reflect its installed state rather than stale "available" data.
            if (result.Success)
                marketplaceService.InvalidatePluginCache(id);

            return Ok(new MarketplaceInstallResult(
                result.Success,
                id,
                result.InstalledVersion ?? "",
                RestartRequired: true,
                result.ErrorMessage));
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete marketplace temp package: {TempPath}", tempPath);
                }
            }
        }
    }

    // ── Single Plugin Update Check ─────────────────────────────────────────────

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

        var descriptor = pluginRegistry.GetById(id);
        if (descriptor is null) return NotFound();

        var manifest = await updateService.CheckAsync(id, descriptor.Version, ct);
        if (manifest is null) return NoContent();
        return Ok(MapUpdateManifest(manifest));
    }

    // ── Bulk Update Check ──────────────────────────────────────────────────────

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

        var dtos = manifests
            .Where(m => !request.UpdatesOnly || m.AvailableVersion != m.InstalledVersion)
            .Select(MapUpdateManifest)
            .ToList();

        return Ok(new BulkUpdateCheckResult(
            TotalChecked:     manifests.Count,
            UpdatesAvailable: dtos.Count,
            Updates:          dtos));
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
