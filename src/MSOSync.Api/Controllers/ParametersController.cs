using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Descriptors;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/parameters")]
public sealed class ParametersController(IParameterMetadataService paramService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetParameters(CancellationToken ct)
    {
        var result = await paramService.GetParametersAsync(ct);
        return Ok(result);
    }

    [HttpGet("descriptors")]
    [Authorize]
    public IActionResult GetDescriptors()
    {
        var descriptors = ParameterDescriptor.Catalog.Values
            .Select(d => new ParameterDescriptorDto(
                d.Name, d.Description, d.IsSecret, d.RequiresRestart, d.IsDynamic))
            .ToList();
        return Ok(descriptors);
    }

    [HttpGet("{name}")]
    [Authorize]
    public async Task<IActionResult> GetParameter(string name, CancellationToken ct)
    {
        var result = await paramService.GetParameterAsync(name, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{name}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateParameter(
        string name, [FromBody] UpdateParameterRequest req, CancellationToken ct)
    {
        await paramService.UpdateParameterAsync(name, req.Value, ct);
        var updated = await paramService.GetParameterAsync(name, ct);
        return Ok(updated);
    }

    [HttpGet("{name}/history")]
    [Authorize]
    public async Task<IActionResult> GetParameterHistory(string name, CancellationToken ct)
    {
        var result = await paramService.GetParameterHistoryAsync(name, ct);
        return Ok(result);
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetAllParameterHistory(CancellationToken ct)
    {
        var result = await paramService.GetAllParameterHistoryAsync(ct);
        return Ok(result);
    }
}
