using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Authorization;
using MSOSync.Api.Dtos.Configuration;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/configuration")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ConfigurationAssignmentController(
    IConfigurationAssignmentService assignmentSvc,
    IRolloutService rolloutSvc,
    INodeAuthorizationService authz,
    IValidator<AssignRequest> assignValidator,
    IValidator<StartRolloutRequest> rolloutValidator,
    IValidator<SetOverrideRequest> overrideValidator) : ControllerBase
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

    [HttpPost("nodes/{nodeId}/assign")]
    [ProducesResponseType(typeof(NodeConfigurationDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Assign(string nodeId, [FromBody] AssignRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await assignValidator.ValidateAndThrowAsync(request, ct);
        var dto = await assignmentSvc.AssignAsync(nodeId, request.TemplateId, request.Version, ActorGuid, null, ct);
        return Ok(dto);
    }

    [HttpDelete("nodes/{nodeId}/assign")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Unassign(string nodeId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await assignmentSvc.UnassignAsync(nodeId, ActorGuid, ct);
        return NoContent();
    }

    [HttpGet("nodes/{nodeId}")]
    [ProducesResponseType(typeof(NodeConfigurationDto), 200)]
    public async Task<IActionResult> GetNodeConfiguration(string nodeId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var dto = await assignmentSvc.GetNodeConfigurationAsync(nodeId, ct);
        return Ok(dto);
    }

    [HttpGet("nodes/{nodeId}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ConfigurationHistoryEventDto>), 200)]
    public async Task<IActionResult> GetNodeHistory(string nodeId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var history = await assignmentSvc.GetNodeHistoryAsync(nodeId, ct);
        return Ok(history);
    }

    [HttpPost("nodes/{nodeId}/overrides")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> SetOverride(string nodeId, [FromBody] SetOverrideRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await overrideValidator.ValidateAndThrowAsync(request, ct);
        await assignmentSvc.SetOverrideAsync(nodeId, request.Key, request.Value, request.Source, ActorGuid, ct);
        return Ok();
    }

    [HttpDelete("nodes/{nodeId}/overrides/{key}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RemoveOverride(string nodeId, string key, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await assignmentSvc.RemoveOverrideAsync(nodeId, key, ActorGuid, ct);
        return NoContent();
    }

    [HttpPost("rollout")]
    [ProducesResponseType(typeof(RolloutAcceptedResponse), 202)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> StartRollout([FromBody] StartRolloutRequest request, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        await rolloutValidator.ValidateAndThrowAsync(request, ct);
        var rollout = await rolloutSvc.StartRolloutAsync(
            request.TemplateId, request.TemplateVersion, request.NodeIds, ActorGuid, ct);
        return Accepted(new RolloutAcceptedResponse(rollout.Id, rollout.Status));
    }

    [HttpGet("rollout/{rolloutId:guid}")]
    [ProducesResponseType(typeof(RolloutDto), 200)]
    public async Task<IActionResult> GetRollout(Guid rolloutId, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var rollout = await rolloutSvc.GetRolloutAsync(rolloutId, ct);
        return Ok(rollout);
    }

    [HttpGet("drift")]
    [ProducesResponseType(typeof(IReadOnlyList<DriftNodeDto>), 200)]
    public async Task<IActionResult> GetDrift(
        [FromQuery] string? state, [FromQuery] Guid? templateId,
        [FromQuery] int? version, [FromQuery] string? nodeGroup,
        [FromQuery] string? search, CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var nodes = await assignmentSvc.GetDriftNodesAsync(state, templateId, version, nodeGroup, search, ct);
        return Ok(nodes);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DriftSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        var summary = await assignmentSvc.GetDriftSummaryAsync(ct);
        return Ok(summary);
    }
}
