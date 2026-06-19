# Epic 4 / Task 9: API Controllers + Validators + AddTriggerRouterRequest

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create 6 ASP.NET Core controllers (`ParametersController`, `NodesController`, `TriggersController`, `RoutersController`, `ChannelsController`, `MetadataController`), the `AddTriggerRouterRequest` DTO, and 4 FluentValidation validators. All validators are auto-discovered by the existing `AddValidatorsFromAssemblyContaining<AuthController>()` call.

**Architecture:** Controllers follow the `AuthController` pattern: `[ApiController]`, primary constructor DI, `ControllerBase`. All reads return `Ok(result)`, creates return `CreatedAtAction`, deletes/rejects return `NoContent()`, enables/disables/approves return `Ok()`. Exception handling is delegated to `GlobalExceptionHandler` (Task 3) — controllers do not catch.

**Tech Stack:** ASP.NET Core 9, FluentValidation 11.11.0

## Global Constraints

- All routes: `api/v1/`
- Controllers in `MSOSync.Api.Controllers` namespace (no subdirectory)
- `AddTriggerRouterRequest` in `MSOSync.Api.Dtos.Nodes` namespace
- Validators in `MSOSync.Api.Validators` namespace
- Authorization levels:
  - GET endpoints: `[Authorize]`
  - POST/PUT (create, update, enable, disable, approve): `[Authorize(Policy = "OperatorOrAbove")]`
  - DELETE / reject: `[Authorize(Policy = "AdminOnly")]`
  - `PUT /parameters/{name}`: `[Authorize(Policy = "AdminOnly")]`
  - `GET /nodes/{nodeId}/security`: `[Authorize(Policy = "AdminOnly")]`
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Api/Dtos/Nodes/AddTriggerRouterRequest.cs`
- Create: `src/MSOSync.Api/Controllers/ParametersController.cs`
- Create: `src/MSOSync.Api/Controllers/NodesController.cs`
- Create: `src/MSOSync.Api/Controllers/TriggersController.cs`
- Create: `src/MSOSync.Api/Controllers/RoutersController.cs`
- Create: `src/MSOSync.Api/Controllers/ChannelsController.cs`
- Create: `src/MSOSync.Api/Controllers/MetadataController.cs`
- Create: `src/MSOSync.Api/Validators/CreateTriggerRequestValidator.cs`
- Create: `src/MSOSync.Api/Validators/CreateRouterRequestValidator.cs`
- Create: `src/MSOSync.Api/Validators/CreateChannelRequestValidator.cs`
- Create: `src/MSOSync.Api/Validators/UpdateParameterRequestValidator.cs`

**Interfaces:**
- Consumes: `IParameterMetadataService`, `INodeMetadataService`, `ITriggerMetadataService`, `IRouterMetadataService`, `IChannelMetadataService` (Tasks 4–7), all Metadata DTOs (Tasks 4–7)
- Produces: 6 REST controllers + 4 validators — consumed by Tasks 10 and 11 (tests)

---

- [ ] **Step 1: Create `AddTriggerRouterRequest`**

```csharp
// src/MSOSync.Api/Dtos/Nodes/AddTriggerRouterRequest.cs
namespace MSOSync.Api.Dtos.Nodes;

public sealed record AddTriggerRouterRequest(string RouterId);
```

- [ ] **Step 2: Create `ParametersController`**

```csharp
// src/MSOSync.Api/Controllers/ParametersController.cs
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
```

- [ ] **Step 3: Create `NodesController`**

```csharp
// src/MSOSync.Api/Controllers/NodesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/nodes")]
public sealed class NodesController(INodeMetadataService nodeService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
    {
        var result = await nodeService.GetNodesAsync(ct);
        return Ok(result);
    }

    [HttpGet("{nodeId}")]
    [Authorize]
    public async Task<IActionResult> GetNode(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeAsync(nodeId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{nodeId}/security")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetNodeSecurity(string nodeId, CancellationToken ct)
    {
        var result = await nodeService.GetNodeSecurityInfoAsync(nodeId, ct);
        return Ok(result);
    }

    [HttpGet("groups")]
    [Authorize]
    public async Task<IActionResult> GetNodeGroups(CancellationToken ct)
    {
        var result = await nodeService.GetNodeGroupsAsync(ct);
        return Ok(result);
    }

    [HttpPut("{nodeId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateNode(
        string nodeId, [FromBody] UpdateNodeRequest req, CancellationToken ct)
    {
        var result = await nodeService.UpdateNodeAsync(nodeId, req, ct);
        return Ok(result);
    }

    [HttpPost("{nodeId}/enable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> EnableNode(string nodeId, CancellationToken ct)
    {
        await nodeService.EnableNodeAsync(nodeId, ct);
        return Ok();
    }

    [HttpPost("{nodeId}/disable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> DisableNode(string nodeId, CancellationToken ct)
    {
        await nodeService.DisableNodeAsync(nodeId, ct);
        return Ok();
    }

    [HttpGet("registrations/pending")]
    [Authorize]
    public async Task<IActionResult> GetPendingRegistrations(CancellationToken ct)
    {
        var result = await nodeService.GetPendingRegistrationsAsync(ct);
        return Ok(result);
    }

    [HttpPost("registrations/{requestId:long}/approve")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> ApproveRegistration(long requestId, CancellationToken ct)
    {
        var result = await nodeService.ApproveRegistrationAsync(requestId, ct);
        return Ok(result);
    }

    [HttpDelete("registrations/{requestId:long}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectRegistration(long requestId, CancellationToken ct)
    {
        await nodeService.RejectRegistrationAsync(requestId, ct);
        return NoContent();
    }
}
```

- [ ] **Step 4: Create `TriggersController`**

```csharp
// src/MSOSync.Api/Controllers/TriggersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/triggers")]
public sealed class TriggersController(ITriggerMetadataService triggerService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetTriggers(CancellationToken ct)
    {
        var result = await triggerService.GetTriggersAsync(ct);
        return Ok(result);
    }

    [HttpGet("{triggerId}")]
    [Authorize]
    public async Task<IActionResult> GetTrigger(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerAsync(triggerId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("channel/{channelId}")]
    [Authorize]
    public async Task<IActionResult> GetTriggersForChannel(string channelId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggersForChannelAsync(channelId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> CreateTrigger([FromBody] CreateTriggerRequest req, CancellationToken ct)
    {
        var result = await triggerService.CreateTriggerAsync(req, ct);
        return CreatedAtAction(nameof(GetTrigger), new { triggerId = result.TriggerId }, result);
    }

    [HttpPut("{triggerId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateTrigger(
        string triggerId, [FromBody] UpdateTriggerRequest req, CancellationToken ct)
    {
        var result = await triggerService.UpdateTriggerAsync(triggerId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{triggerId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.DeleteTriggerAsync(triggerId, ct);
        return NoContent();
    }

    [HttpPost("{triggerId}/enable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> EnableTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.EnableTriggerAsync(triggerId, ct);
        return Ok();
    }

    [HttpPost("{triggerId}/disable")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> DisableTrigger(string triggerId, CancellationToken ct)
    {
        await triggerService.DisableTriggerAsync(triggerId, ct);
        return Ok();
    }

    [HttpGet("{triggerId}/routers")]
    [Authorize]
    public async Task<IActionResult> GetTriggerRouters(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerRoutersAsync(triggerId, ct);
        return Ok(result);
    }

    [HttpPost("{triggerId}/routers")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> AddTriggerRouter(
        string triggerId, [FromBody] AddTriggerRouterRequest req, CancellationToken ct)
    {
        await triggerService.AddTriggerRouterAsync(triggerId, req.RouterId, ct);
        return Ok();
    }

    [HttpDelete("{triggerId}/routers/{routerId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RemoveTriggerRouter(
        string triggerId, string routerId, CancellationToken ct)
    {
        await triggerService.RemoveTriggerRouterAsync(triggerId, routerId, ct);
        return NoContent();
    }

    [HttpGet("{triggerId}/history")]
    [Authorize]
    public async Task<IActionResult> GetTriggerHistory(string triggerId, CancellationToken ct)
    {
        var result = await triggerService.GetTriggerHistoryAsync(triggerId, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 5: Create `RoutersController`**

```csharp
// src/MSOSync.Api/Controllers/RoutersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/routers")]
public sealed class RoutersController(IRouterMetadataService routerService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetRouters(CancellationToken ct)
    {
        var result = await routerService.GetRoutersAsync(ct);
        return Ok(result);
    }

    [HttpGet("{routerId}")]
    [Authorize]
    public async Task<IActionResult> GetRouter(string routerId, CancellationToken ct)
    {
        var result = await routerService.GetRouterAsync(routerId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("source/{groupId}")]
    [Authorize]
    public async Task<IActionResult> GetRoutersForSourceGroup(string groupId, CancellationToken ct)
    {
        var result = await routerService.GetRoutersForSourceGroupAsync(groupId, ct);
        return Ok(result);
    }

    [HttpGet("target/{groupId}")]
    [Authorize]
    public async Task<IActionResult> GetRoutersForTargetGroup(string groupId, CancellationToken ct)
    {
        var result = await routerService.GetRoutersForTargetGroupAsync(groupId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> CreateRouter([FromBody] CreateRouterRequest req, CancellationToken ct)
    {
        var result = await routerService.CreateRouterAsync(req, ct);
        return CreatedAtAction(nameof(GetRouter), new { routerId = result.RouterId }, result);
    }

    [HttpPut("{routerId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateRouter(
        string routerId, [FromBody] UpdateRouterRequest req, CancellationToken ct)
    {
        var result = await routerService.UpdateRouterAsync(routerId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{routerId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteRouter(string routerId, CancellationToken ct)
    {
        await routerService.DeleteRouterAsync(routerId, ct);
        return NoContent();
    }
}
```

- [ ] **Step 6: Create `ChannelsController`**

```csharp
// src/MSOSync.Api/Controllers/ChannelsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/channels")]
public sealed class ChannelsController(IChannelMetadataService channelService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
    {
        var result = await channelService.GetChannelsAsync(ct);
        return Ok(result);
    }

    [HttpGet("{channelId}")]
    [Authorize]
    public async Task<IActionResult> GetChannel(string channelId, CancellationToken ct)
    {
        var result = await channelService.GetChannelAsync(channelId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateChannelRequest req, CancellationToken ct)
    {
        var result = await channelService.CreateChannelAsync(req, ct);
        return CreatedAtAction(nameof(GetChannel), new { channelId = result.ChannelId }, result);
    }

    [HttpPut("{channelId}")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> UpdateChannel(
        string channelId, [FromBody] UpdateChannelRequest req, CancellationToken ct)
    {
        var result = await channelService.UpdateChannelAsync(channelId, req, ct);
        return Ok(result);
    }

    [HttpDelete("{channelId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteChannel(string channelId, CancellationToken ct)
    {
        await channelService.DeleteChannelAsync(channelId, ct);
        return NoContent();
    }
}
```

- [ ] **Step 7: Create `MetadataController`**

```csharp
// src/MSOSync.Api/Controllers/MetadataController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Interfaces;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metadata")]
public sealed class MetadataController(
    INodeMetadataService nodes,
    ITriggerMetadataService triggers,
    IRouterMetadataService routers,
    IChannelMetadataService channels,
    IParameterMetadataService parameters) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var allNodes      = await nodes.GetNodesAsync(ct);
        var allTriggers   = await triggers.GetTriggersAsync(ct);
        var allRouters    = await routers.GetRoutersAsync(ct);
        var allChannels   = await channels.GetChannelsAsync(ct);
        var allParameters = await parameters.GetParametersAsync(ct);

        return Ok(new
        {
            nodes      = allNodes.Count,
            triggers   = allTriggers.Count,
            routers    = allRouters.Count,
            channels   = allChannels.Count,
            parameters = allParameters.Count
        });
    }
}
```

- [ ] **Step 8: Create validators**

```csharp
// src/MSOSync.Api/Validators/CreateTriggerRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateTriggerRequestValidator : AbstractValidator<CreateTriggerRequest>
{
    public CreateTriggerRequestValidator()
    {
        RuleFor(x => x.TriggerId)
            .NotEmpty().WithMessage("TriggerId is required")
            .MaximumLength(50).WithMessage("TriggerId must be at most 50 characters")
            .Matches(@"^[a-z0-9_\-]+$").WithMessage("TriggerId must contain only lowercase letters, digits, underscores, and hyphens");

        RuleFor(x => x.SourceTable)
            .NotEmpty().WithMessage("SourceTable is required")
            .MaximumLength(128).WithMessage("SourceTable must be at most 128 characters");

        RuleFor(x => x.ChannelId)
            .NotEmpty().WithMessage("ChannelId is required")
            .MaximumLength(50).WithMessage("ChannelId must be at most 50 characters");
    }
}
```

```csharp
// src/MSOSync.Api/Validators/CreateRouterRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateRouterRequestValidator : AbstractValidator<CreateRouterRequest>
{
    private static readonly string[] ValidRouterTypes = ["default", "column", "subselect"];

    public CreateRouterRequestValidator()
    {
        RuleFor(x => x.RouterId)
            .NotEmpty().WithMessage("RouterId is required")
            .MaximumLength(50).WithMessage("RouterId must be at most 50 characters");

        RuleFor(x => x.SourceNodeGroup)
            .NotEmpty().WithMessage("SourceNodeGroup is required")
            .MaximumLength(50).WithMessage("SourceNodeGroup must be at most 50 characters");

        RuleFor(x => x.TargetNodeGroup)
            .NotEmpty().WithMessage("TargetNodeGroup is required")
            .MaximumLength(50).WithMessage("TargetNodeGroup must be at most 50 characters");

        RuleFor(x => x.RouterType)
            .NotEmpty().WithMessage("RouterType is required")
            .Must(t => ValidRouterTypes.Contains(t))
            .WithMessage("RouterType must be one of: default, column, subselect");
    }
}
```

```csharp
// src/MSOSync.Api/Validators/CreateChannelRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(x => x.ChannelId)
            .NotEmpty().WithMessage("ChannelId is required")
            .MaximumLength(50).WithMessage("ChannelId must be at most 50 characters");

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, 10000).WithMessage("BatchSize must be between 1 and 10000");

        RuleFor(x => x.MaxBatchToSend)
            .InclusiveBetween(1, 100).WithMessage("MaxBatchToSend must be between 1 and 100");

        RuleFor(x => x.MaxDataSize)
            .InclusiveBetween(1024L, 104857600L)
            .WithMessage("MaxDataSize must be between 1024 (1KB) and 104857600 (100MB)");

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 1000).WithMessage("Priority must be between 1 and 1000");
    }
}
```

```csharp
// src/MSOSync.Api/Validators/UpdateParameterRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Validators;

public sealed class UpdateParameterRequestValidator : AbstractValidator<UpdateParameterRequest>
{
    public UpdateParameterRequestValidator()
    {
        RuleFor(x => x.Value)
            .NotNull().WithMessage("Value is required")
            .MaximumLength(4000).WithMessage("Value must be at most 4000 characters");
    }
}
```

- [ ] **Step 9: Build `MSOSync.Api` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Api\MSOSync.Api.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 10: Build full solution**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 11: Commit**

```powershell
git add src/MSOSync.Api/Dtos/Nodes/AddTriggerRouterRequest.cs
git add src/MSOSync.Api/Controllers/ParametersController.cs
git add src/MSOSync.Api/Controllers/NodesController.cs
git add src/MSOSync.Api/Controllers/TriggersController.cs
git add src/MSOSync.Api/Controllers/RoutersController.cs
git add src/MSOSync.Api/Controllers/ChannelsController.cs
git add src/MSOSync.Api/Controllers/MetadataController.cs
git add src/MSOSync.Api/Validators/CreateTriggerRequestValidator.cs
git add src/MSOSync.Api/Validators/CreateRouterRequestValidator.cs
git add src/MSOSync.Api/Validators/CreateChannelRequestValidator.cs
git add src/MSOSync.Api/Validators/UpdateParameterRequestValidator.cs
git commit -m "feat(api): add metadata controllers, validators, and AddTriggerRouterRequest"
```
