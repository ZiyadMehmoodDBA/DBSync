using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Export;
using MSOSync.Common;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/export-jobs")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ExportJobController(
    IExportJobService     jobService,
    ICurrentUserService   currentUser,
    IPermissionService    permissionService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(202)]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateExportJobRequest request, CancellationToken ct)
    {
        var exportPerms = await permissionService.GetEffectivePermissionsAsync(currentUser.GetCurrentUsername(), ct);
        if (!exportPerms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        if (!string.IsNullOrEmpty(request.FiltersJson))
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                switch (request.ResourceType)
                {
                    case "events":
                        var ef = JsonSerializer.Deserialize<EventFilter>(request.FiltersJson, jsonOptions);
                        if (ef is null) return BadRequest("Invalid filtersJson for events");
                        break;
                    case "incoming-batches":
                        var ibf = JsonSerializer.Deserialize<IncomingBatchFilter>(request.FiltersJson, jsonOptions);
                        if (ibf is null) return BadRequest("Invalid filtersJson for incoming-batches");
                        break;
                    case "audit":
                        var af = JsonSerializer.Deserialize<AuditFilter>(request.FiltersJson, jsonOptions);
                        if (af is null) return BadRequest("Invalid filtersJson for audit");
                        break;
                    default:
                        return BadRequest($"Unknown resourceType: {request.ResourceType}");
                }
            }
            catch (JsonException)
            {
                return BadRequest("Invalid filtersJson: malformed JSON");
            }
        }
        else if (!new[] { "events", "incoming-batches", "audit" }.Contains(request.ResourceType))
        {
            return BadRequest($"Unknown resourceType: {request.ResourceType}");
        }

        var username = currentUser.GetCurrentUsername();
        var job = await jobService.CreateJobAsync(
            username,
            request.ResourceType,
            request.Format,
            request.FiltersJson,
            request.ParentJobId,
            ct);
        return StatusCode(202, new { jobId = job.JobId });
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExportJobDto>), 200)]
    public async Task<IActionResult> GetJobs(
        [FromQuery] bool all = false, CancellationToken ct = default)
    {
        var username = currentUser.GetCurrentUsername();
        var exportPerms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!exportPerms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        IReadOnlyList<SyncExportJob> jobs;
        if (all)
        {
            if (!exportPerms.Permissions.Contains(SystemPermissions.ManageUsers))
                return Forbid();
            jobs = await jobService.GetAllJobsAsync(ct);
        }
        else
        {
            jobs = await jobService.GetJobsForUserAsync(username, ct);
        }

        return Ok(jobs.Select(ToDto));
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!perms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        var job = await jobService.GetJobAsync(id, ct);
        if (job is null || job.Status is ExportJobStatus.Deleted or ExportJobStatus.Expired)
            return NotFound();

        if (job.RequestedBy != username && !perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        if (job.OutputPath is null || !System.IO.File.Exists(job.OutputPath))
            return NotFound();

        var contentType = job.Format == "json" ? "application/json" : "text/csv";
        var fileName    = $"{job.ResourceType}-export-{job.JobId}.{job.Format}";
        return PhysicalFile(job.OutputPath, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var perms = await permissionService.GetEffectivePermissionsAsync(username, ct);
        if (!perms.Permissions.Contains(SystemPermissions.ExportData))
            return Forbid();

        var job = await jobService.GetJobAsync(id, ct);
        if (job is null) return NotFound();

        if (job.RequestedBy != username && !perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        await jobService.SoftDeleteJobAsync(id, ct);
        return NoContent();
    }

    private static ExportJobDto ToDto(SyncExportJob j) => new(
        j.JobId, j.ParentJobId, j.RequestedBy, j.ResourceType, j.Format,
        j.Status, j.ProgressPercent, j.RowCount, j.ErrorMessage,
        j.ExpiresAt, j.CreatedAt, j.StartedAt, j.CompletedAt);
}
