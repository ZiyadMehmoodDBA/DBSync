using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/configuration/templates")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ConfigurationTemplateController(
    IConfigurationTemplateService templateSvc,
    INodeAuthorizationService authz,
    IValidator<CreateTemplateRequest> createValidator,
    IValidator<UpdateDraftRequest> updateValidator) : ControllerBase
{
    private Guid ActorGuid => ClaimsToGuid(User.FindFirstValue("userId"));

    private static Guid ClaimsToGuid(string? claim)
    {
        if (long.TryParse(claim, out var id))
        {
            var bytes = new byte[16];
            BitConverter.TryWriteBytes(bytes.AsSpan(), id);
            return new Guid(bytes);
        }
        return Guid.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var templates = await templateSvc.ListAsync(status, ct);
        return Ok(templates);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTemplateRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await createValidator.ValidateAndThrowAsync(request, ct);
        var template = await templateSvc.CreateAsync(request, ActorGuid, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var template = await templateSvc.GetAsync(id, ct);
        return Ok(template);
    }

    [HttpPut("{id:guid}/draft")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] UpdateDraftRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var rowVersion = string.IsNullOrEmpty(ifMatch)
            ? Array.Empty<byte>()
            : Convert.FromBase64String(ifMatch.Trim('"'));
        var version = await templateSvc.UpdateDraftAsync(id, request, rowVersion, ActorGuid, ct);
        return Ok(version);
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var result = await templateSvc.ValidatePreviewAsync(id, ct);
        if (!result.Validation.IsValid)
            return UnprocessableEntity(new
            {
                errors = result.Validation.Errors.Select(e => new { field = e.Field, message = e.Message })
            });
        return Ok(new { hashPreview = result.HashPreview, effectiveSettings = result.EffectiveSettings });
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var version = await templateSvc.PublishAsync(id, ActorGuid, ct);
        return Ok(version);
    }

    [HttpPost("{id:guid}/clone")]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneTemplateRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var template = await templateSvc.CloneAsync(id, request.NewName, ActorGuid, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await templateSvc.ArchiveAsync(id, ActorGuid, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var versions = await templateSvc.ListVersionsAsync(id, ct);
        return Ok(versions);
    }

    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<IActionResult> GetVersion(Guid id, int version, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var dto = await templateSvc.GetVersionAsync(id, version, ct);
        return Ok(dto);
    }

    [HttpGet("{id:guid}/versions/{v1:int}/diff/{v2:int}")]
    public async Task<IActionResult> DiffVersions(Guid id, int v1, int v2, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var ver1 = await templateSvc.GetVersionAsync(id, v1, ct);
        var ver2 = await templateSvc.GetVersionAsync(id, v2, ct);
        return Ok(new { version1 = ver1, version2 = ver2 });
    }
}
