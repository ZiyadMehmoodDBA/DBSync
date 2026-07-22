using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Api.Dtos.Configuration;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Configuration.Dtos;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/configuration/templates")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ConfigurationTemplateController(
    IConfigurationTemplateService    templateSvc,
    INodeAuthorizationService        authz,
    IConfigurationComparisonService  comparisonSvc,
    IValidator<CreateTemplateRequest> createValidator,
    IValidator<UpdateDraftRequest>   updateValidator) : ControllerBase
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
    [ProducesResponseType(typeof(IReadOnlyList<TemplateSummaryDto>), 200)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var templates = await templateSvc.ListAsync(status, ct);
        return Ok(templates);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TemplateDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Create([FromBody] CreateTemplateRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await createValidator.ValidateAndThrowAsync(request, ct);
        var template = await templateSvc.CreateAsync(request, ActorGuid, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TemplateDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var template = await templateSvc.GetAsync(id, ct);
        return Ok(template);
    }

    [HttpPut("{id:guid}/draft")]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
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
    [ProducesResponseType(typeof(ValidationPreviewResponse), 200)]
    [ProducesResponseType(typeof(ValidationErrorsResponse), 422)]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var result = await templateSvc.ValidatePreviewAsync(id, ct);
        if (!result.Validation.IsValid)
            return UnprocessableEntity(new ValidationErrorsResponse(result.Validation.Errors));
        return Ok(new ValidationPreviewResponse(result.HashPreview, result.EffectiveSettings));
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var version = await templateSvc.PublishAsync(id, ActorGuid, ct);
        return Ok(version);
    }

    [HttpPost("{id:guid}/clone")]
    [ProducesResponseType(typeof(TemplateDto), 201)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneTemplateRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var template = await templateSvc.CloneAsync(id, request.NewName, ActorGuid, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await templateSvc.ArchiveAsync(id, ActorGuid, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateVersionSummaryDto>), 200)]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var versions = await templateSvc.ListVersionsAsync(id, ct);
        return Ok(versions);
    }

    [HttpGet("{id:guid}/versions/{version:int}")]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    public async Task<IActionResult> GetVersion(Guid id, int version, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var dto = await templateSvc.GetVersionAsync(id, version, ct);
        return Ok(dto);
    }

    [HttpGet("{id:guid}/versions/{v1:int}/diff/{v2:int}")]
    [ProducesResponseType(typeof(TemplateVersionDiffResponse), 200)]
    public async Task<IActionResult> DiffVersions(Guid id, int v1, int v2, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var ver1 = await templateSvc.GetVersionAsync(id, v1, ct);
        var ver2 = await templateSvc.GetVersionAsync(id, v2, ct);
        return Ok(new TemplateVersionDiffResponse(ver1, ver2));
    }

    [HttpGet("{id:guid}/compare")]
    [ProducesResponseType(typeof(ConfigVersionDiffDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Compare(
        Guid id,
        [FromQuery] int v1,
        [FromQuery] int v2,
        CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        if (v1 == v2) return BadRequest(new ProblemDetails { Title = "v1 and v2 must differ." });
        var diff = await comparisonSvc.CompareAsync(id, v1, v2, ct);
        return Ok(diff);
    }
}
