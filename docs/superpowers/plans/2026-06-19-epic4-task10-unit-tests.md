# Epic 4 / Task 10: MSOSync.MetadataTests — Unit Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the `MSOSync.MetadataTests` test project using SQLite in-memory (NOT EF InMemory) for isolation, write unit tests for all 5 metadata services, and add the project to the solution.

**Architecture:** SQLite in-memory via `"DataSource=:memory:"`. Each test method creates its own `AppDbContext` instance by calling `TestDbContext.Create()`. MediatR and `ICurrentUserService` are mocked with Moq. No global test fixtures — each test is fully isolated.

**Tech Stack:** xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72, Microsoft.EntityFrameworkCore.Sqlite 9.0.0

## Global Constraints

- No EF InMemory provider — use SQLite `"DataSource=:memory:"`
- `db.Database.OpenConnection()` + `db.Database.EnsureCreated()` pattern required for SQLite
- Each test creates its own `AppDbContext` — no shared state
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- `Microsoft.EntityFrameworkCore.Sqlite` Version="9.0.0" must be added to `Directory.Packages.props` if not already present (check first — it may already be there)
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Modify: `Directory.Packages.props` — add `Microsoft.EntityFrameworkCore.Sqlite` Version="9.0.0" to Testing group (if not present)
- Create: `tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj`
- Modify: `MSOSync.sln` — add new project
- Create: `tests/MSOSync.MetadataTests/TestDbContext.cs`
- Create: `tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs`
- Create: `tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs`
- Create: `tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs`
- Create: `tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs`
- Create: `tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs`

**Interfaces:**
- Consumes: All 5 `I*MetadataService` implementations (Tasks 4–7), `AppDbContext` (Persistence), `SyncException` hierarchy (Task 3)
- Produces: Passing test suite — consumed by CI

---

- [ ] **Step 1: Add `Microsoft.EntityFrameworkCore.Sqlite` to `Directory.Packages.props`**

Check if already present:
```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
Select-String "EntityFrameworkCore.Sqlite" Directory.Packages.props
```

If NOT present, add inside the `<ItemGroup Label="Testing">` block:
```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
```

- [ ] **Step 2: Create `MSOSync.MetadataTests.csproj`**

```xml
<!-- tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Security\MSOSync.Security.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add project to solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet sln MSOSync.sln add tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --solution-folder tests
```

- [ ] **Step 4: Create `TestDbContext` helper**

```csharp
// tests/MSOSync.MetadataTests/TestDbContext.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.MetadataTests;

internal static class TestDbContext
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

- [ ] **Step 5: Create `ParameterMetadataServiceTests`**

```csharp
// tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class ParameterMetadataServiceTests
{
    private static ParameterMetadataService CreateService(
        out MSOSync.Persistence.AppDbContext db,
        Mock<IMediator>? mediatorMock = null,
        Mock<ICurrentUserService>? userMock = null)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        var user = (userMock ?? new Mock<ICurrentUserService>()).Object;
        return new ParameterMetadataService(db, cache, mediator, user);
    }

    [Fact]
    public async Task UpdateParameterAsync_KnownParameter_WritesHistoryRow()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");

        var hist = db.ParameterHists.Single();
        hist.ParameterName.Should().Be("sync.batch.size");
        hist.OldValue.Should().Be("100");
        hist.NewValue.Should().Be("200");
    }

    [Fact]
    public async Task UpdateParameterAsync_PublishesParameterChangedEvent()
    {
        var mediatorMock = new Mock<IMediator>();
        var svc = CreateService(out var db, mediatorMock);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");

        mediatorMock.Verify(m => m.Publish(
            It.Is<ParameterChangedEvent>(e => e.ParameterName == "sync.batch.size"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateParameterAsync_AfterUpdate_CacheReturnsNewValue()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        var before = await svc.GetParameterAsync("sync.batch.size");
        before!.Value.Should().Be("100");

        await svc.UpdateParameterAsync("sync.batch.size", "999");

        var after = await svc.GetParameterAsync("sync.batch.size");
        after!.Value.Should().Be("999");
    }

    [Fact]
    public async Task UpdateParameterAsync_UnknownName_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.UpdateParameterAsync("unknown.param", "value");

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*unknown.param*");
    }

    [Fact]
    public async Task GetAllParameterHistoryAsync_ReturnsAllRows()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.max.retries", ParameterValue = "3" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");
        await svc.UpdateParameterAsync("sync.max.retries", "5");

        var all = await svc.GetAllParameterHistoryAsync();
        all.Should().HaveCount(2);
    }
}
```

- [ ] **Step 6: Create `NodeMetadataServiceTests`**

```csharp
// tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class NodeMetadataServiceTests
{
    private static (NodeMetadataService Svc, AppDbContext Db, BCryptPasswordHasher Hasher) CreateService(
        Mock<IMediator>? mediatorMock = null)
    {
        var db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        var hasher = new BCryptPasswordHasher();
        var nodeSecurity = new NodeSecurityService(db, hasher);
        var svc = new NodeMetadataService(db, cache, mediator, nodeSecurity);
        return (svc, db, hasher);
    }

    [Fact]
    public async Task ApproveRegistrationAsync_ValidRequest_CreatesNodeAndReturnsToken()
    {
        var (svc, db, _) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-1",
            NodeGroup = "default",
            SyncUrl = "http://node1:8080",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        var result = await svc.ApproveRegistrationAsync(request.RequestId);

        result.NodeId.Should().Be("node-1");
        result.RawToken.Should().NotBeNullOrEmpty();
        db.Nodes.Should().ContainSingle(n => n.NodeId == "node-1");
        db.NodeSecurities.Should().ContainSingle(s => s.NodeId == "node-1");
    }

    [Fact]
    public async Task ApproveRegistrationAsync_TokenVerifies_BCryptHashMatch()
    {
        var (svc, db, hasher) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-2",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        var result = await svc.ApproveRegistrationAsync(request.RequestId);

        var sec = db.NodeSecurities.Single(s => s.NodeId == "node-2");
        hasher.Verify(result.RawToken, sec.CurrentTokenHash).Should().BeTrue();
    }

    [Fact]
    public async Task RejectRegistrationAsync_RemovesRegistrationRequest()
    {
        var (svc, db, _) = CreateService();
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-3",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        await svc.RejectRegistrationAsync(request.RequestId);

        db.RegistrationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableNodeAsync_SetsEnabledTrue()
    {
        var (svc, db, _) = CreateService();
        db.Nodes.Add(new SyncNode
        {
            NodeId = "node-4", GroupId = "default",
            SyncUrl = "http://n4", Status = "APPROVED",
            SyncEnabled = false
        });
        await db.SaveChangesAsync();

        await svc.EnableNodeAsync("node-4");

        db.Nodes.Single().SyncEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DisableNodeAsync_SetsEnabledFalse()
    {
        var (svc, db, _) = CreateService();
        db.Nodes.Add(new SyncNode
        {
            NodeId = "node-5", GroupId = "default",
            SyncUrl = "http://n5", Status = "APPROVED",
            SyncEnabled = true
        });
        await db.SaveChangesAsync();

        await svc.DisableNodeAsync("node-5");

        db.Nodes.Single().SyncEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetNodeSecurityInfoAsync_NeverReturnsHashValues()
    {
        var (svc, db, _) = CreateService();
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "node-6",
            CurrentTokenHash = "hashed-value-here",
            CreatedTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.GetNodeSecurityInfoAsync("node-6");

        result.NodeId.Should().Be("node-6");
        // DTO has no hash fields — just structural check
        result.HasPendingRotation.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveRegistrationAsync_NonExistentRequest_ThrowsNotFoundException()
    {
        var (svc, _, _) = CreateService();

        var act = () => svc.ApproveRegistrationAsync(99999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ApproveRegistrationAsync_PublishesNodeMetadataChangedEvent()
    {
        var mediatorMock = new Mock<IMediator>();
        var (svc, db, _) = CreateService(mediatorMock);
        db.RegistrationRequests.Add(new SyncRegistrationRequest
        {
            NodeId = "node-7",
            RequestTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = db.RegistrationRequests.Single();
        await svc.ApproveRegistrationAsync(request.RequestId);

        mediatorMock.Verify(m => m.Publish(
            It.Is<NodeMetadataChangedEvent>(e => e.NodeId == "node-7" && e.Action == "APPROVED"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 7: Create `TriggerMetadataServiceTests`**

```csharp
// tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Services;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class TriggerMetadataServiceTests
{
    private static TriggerMetadataService CreateService(
        out MSOSync.Persistence.AppDbContext db,
        Mock<IMediator>? mediatorMock = null)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        return new TriggerMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateTriggerAsync_WritesHistoryRow()
    {
        var svc = CreateService(out var db);
        var req = new MSOSync.Metadata.Dtos.CreateTriggerRequest("t-1", "dbo.Orders", "default");

        await svc.CreateTriggerAsync(req);

        db.TriggerHists.Should().ContainSingle(h => h.TriggerId == "t-1" && h.TriggerVersion == 1);
    }

    [Fact]
    public async Task CreateTriggerAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-dup", SourceTable = "dbo.T", ChannelId = "ch", TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        var act = () => svc.CreateTriggerAsync(
            new MSOSync.Metadata.Dtos.CreateTriggerRequest("t-dup", "dbo.Other", "ch"));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task UpdateTriggerAsync_BumpsTriggerVersion()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-2", SourceTable = "dbo.T", ChannelId = "ch", TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        var req = new MSOSync.Metadata.Dtos.UpdateTriggerRequest("dbo.T", "ch", true, true, false);
        var result = await svc.UpdateTriggerAsync("t-2", req);

        result.TriggerVersion.Should().Be(2);
        db.TriggerHists.Should().ContainSingle(h => h.TriggerId == "t-2" && h.TriggerVersion == 2);
    }

    [Fact]
    public async Task GetTriggersForChannelAsync_ReturnsOnlyMatchingChannel()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger { TriggerId = "t-a", SourceTable = "A", ChannelId = "ch1", TriggerVersion = 1 });
        db.Triggers.Add(new SyncTrigger { TriggerId = "t-b", SourceTable = "B", ChannelId = "ch2", TriggerVersion = 1 });
        await db.SaveChangesAsync();

        var result = await svc.GetTriggersForChannelAsync("ch1");

        result.Should().ContainSingle(t => t.TriggerId == "t-a");
        result.Should().NotContain(t => t.TriggerId == "t-b");
    }

    [Fact]
    public async Task EnableTriggerAsync_SetsEnabledTrue()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-3", SourceTable = "dbo.T", ChannelId = "ch", Enabled = false, TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        await svc.EnableTriggerAsync("t-3");

        db.Triggers.Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task DisableTriggerAsync_SetsEnabledFalse()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-4", SourceTable = "dbo.T", ChannelId = "ch", Enabled = true, TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        await svc.DisableTriggerAsync("t-4");

        db.Triggers.Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task AddTriggerRouterAsync_CreatesTriggerRouterRow()
    {
        var svc = CreateService(out var db);

        await svc.AddTriggerRouterAsync("t-5", "r-1");

        db.TriggerRouters.Should().ContainSingle(tr => tr.TriggerId == "t-5" && tr.RouterId == "r-1");
    }

    [Fact]
    public async Task RemoveTriggerRouterAsync_DeletesTriggerRouterRow()
    {
        var svc = CreateService(out var db);
        db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = "t-6", RouterId = "r-2", Enabled = true });
        await db.SaveChangesAsync();

        await svc.RemoveTriggerRouterAsync("t-6", "r-2");

        db.TriggerRouters.Should().BeEmpty();
    }
}
```

- [ ] **Step 8: Create `RouterMetadataServiceTests`**

```csharp
// tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Services;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class RouterMetadataServiceTests
{
    private static RouterMetadataService CreateService(out MSOSync.Persistence.AppDbContext db)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = new Mock<IMediator>().Object;
        return new RouterMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateRouterAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter
        {
            RouterId = "r-dup", SourceNodeGroup = "g1", TargetNodeGroup = "g2", RouterType = "default"
        });
        await db.SaveChangesAsync();

        var act = () => svc.CreateRouterAsync(
            new CreateRouterRequest("r-dup", "g1", "g2", "default"));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task GetRoutersForSourceGroupAsync_FiltersCorrectly()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter { RouterId = "r-1", SourceNodeGroup = "src", TargetNodeGroup = "tgt", RouterType = "default" });
        db.Routers.Add(new SyncRouter { RouterId = "r-2", SourceNodeGroup = "other", TargetNodeGroup = "tgt", RouterType = "default" });
        await db.SaveChangesAsync();

        var result = await svc.GetRoutersForSourceGroupAsync("src");

        result.Should().ContainSingle(r => r.RouterId == "r-1");
        result.Should().NotContain(r => r.RouterId == "r-2");
    }

    [Fact]
    public async Task GetRoutersForTargetGroupAsync_FiltersCorrectly()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter { RouterId = "r-3", SourceNodeGroup = "src", TargetNodeGroup = "tgt-a", RouterType = "default" });
        db.Routers.Add(new SyncRouter { RouterId = "r-4", SourceNodeGroup = "src", TargetNodeGroup = "tgt-b", RouterType = "default" });
        await db.SaveChangesAsync();

        var result = await svc.GetRoutersForTargetGroupAsync("tgt-a");

        result.Should().ContainSingle(r => r.RouterId == "r-3");
    }

    [Fact]
    public async Task DeleteRouterAsync_NonExistent_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.DeleteRouterAsync("no-such-router");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 9: Create `ChannelMetadataServiceTests`**

```csharp
// tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Services;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class ChannelMetadataServiceTests
{
    private static ChannelMetadataService CreateService(out MSOSync.Persistence.AppDbContext db)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = new Mock<IMediator>().Object;
        return new ChannelMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateChannelAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Channels.Add(new SyncChannel { ChannelId = "ch-dup", Priority = 1, BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L });
        await db.SaveChangesAsync();

        var act = () => svc.CreateChannelAsync(
            new CreateChannelRequest("ch-dup", 1));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task CreateChannelAsync_ValidRequest_PersiststWithDefaults()
    {
        var svc = CreateService(out var db);

        var result = await svc.CreateChannelAsync(new CreateChannelRequest("ch-1", 5));

        result.ChannelId.Should().Be("ch-1");
        result.Priority.Should().Be(5);
        result.BatchSize.Should().Be(1000);
        result.MaxBatchToSend.Should().Be(10);
        result.MaxDataSize.Should().Be(1048576L);
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateChannelAsync_UpdatesAllFields()
    {
        var svc = CreateService(out var db);
        db.Channels.Add(new SyncChannel { ChannelId = "ch-2", Priority = 1, BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L });
        await db.SaveChangesAsync();

        var result = await svc.UpdateChannelAsync("ch-2", new UpdateChannelRequest(2, 500, 5, 2097152L));

        result.Priority.Should().Be(2);
        result.BatchSize.Should().Be(500);
        result.MaxBatchToSend.Should().Be(5);
        result.MaxDataSize.Should().Be(2097152L);
    }

    [Fact]
    public async Task DeleteChannelAsync_NonExistent_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.DeleteChannelAsync("no-such-channel");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 10: Run tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet test tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --logger "console;verbosity=normal"
```

Expected: all tests pass, 0 failures.

- [ ] **Step 11: Run full test suite**

```powershell
dotnet test MSOSync.sln --no-build
```

Expected: all tests pass including pre-existing tests.

- [ ] **Step 12: Commit**

```powershell
git add Directory.Packages.props
git add tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj
git add MSOSync.sln
git add tests/MSOSync.MetadataTests/TestDbContext.cs
git add tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs
git add tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs
git commit -m "test(metadata): add unit tests for all 5 metadata services using SQLite in-memory"
```
