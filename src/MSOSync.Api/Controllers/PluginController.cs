using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Plugins;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/plugins")]
[Authorize(Policy = "AdminOnly")]
public sealed class PluginController(
    IPluginRegistry registry,
    IPluginStore    store,
    IPluginHost     pluginHost) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PluginDto>), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public IActionResult GetAll()
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new ErrorResponse("Plugin host not yet initialized"));

        var dtos = registry.GetAll().Select(ToDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(PluginSummaryDto), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public IActionResult GetSummary()
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new ErrorResponse("Plugin host not yet initialized"));

        var all = registry.GetAll();
        return Ok(new PluginSummaryDto
        {
            Total             = all.Count,
            Loaded            = all.Count(p => p.Status == PluginStatus.Running),
            Failed            = all.Count(p => p.Status == PluginStatus.Failed),
            Disabled          = all.Count(p => p.Status == PluginStatus.Disabled),
            StartupDurationMs = pluginHost.StartupDurationMs,
            LastScanAt        = pluginHost.StartedAt,
        });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PluginDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public IActionResult GetById(string id)
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new ErrorResponse("Plugin host not yet initialized"));

        var plugin = registry.GetById(id);
        if (plugin == null) return NotFound();
        return Ok(ToDto(plugin));
    }

    [HttpGet("{id}/manifest")]
    [ProducesResponseType(typeof(PluginManifest), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorResponse), 503)]
    public IActionResult GetManifest(string id)
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new ErrorResponse("Plugin host not yet initialized"));

        var plugin = registry.GetById(id);
        if (plugin == null) return NotFound();
        if (plugin.Manifest == null) return NotFound();
        return Ok(plugin.Manifest);
    }

    [HttpPost("{id}/enable")]
    [ProducesResponseType(typeof(PluginActionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Enable(string id, CancellationToken ct)
    {
        await store.SetEnabledAsync(id, true, ct);
        return Ok(new PluginActionResult(Success: true, RestartRequired: true));
    }

    [HttpPost("{id}/disable")]
    [ProducesResponseType(typeof(PluginActionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Disable(string id, CancellationToken ct)
    {
        await store.SetEnabledAsync(id, false, ct);
        return Ok(new PluginActionResult(Success: true, RestartRequired: true));
    }

    private static PluginDto ToDto(PluginDescriptor p) => new()
    {
        PluginId             = p.PluginId,
        Name                 = p.Name,
        Version              = p.Version,
        Status               = p.Status.ToString(),
        LoadDurationMs       = p.LoadDurationMs,
        InitializeDurationMs = p.InitializeDurationMs,
        StartDurationMs      = p.StartDurationMs,
        TotalDurationMs      = p.TotalDurationMs,
        LoadedAt             = p.LoadedAt,
        InitializedAt        = p.InitializedAt,
        StartedAt            = p.StartedAt,
        LastError            = p.ErrorMessage,
        FailureStage         = p.FailureStage,
        HostCompatibility    = p.HostCompatibility,
        Capabilities         = p.Capabilities,
        Permissions          = p.Permissions,
        Dependencies         = p.Dependencies,
    };
}
