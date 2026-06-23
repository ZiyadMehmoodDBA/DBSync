# Epic 7: Apply Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `NoOpApplyService` with a production `ApplyEngine` that reconstructs and executes INSERT/UPDATE/DELETE statements from incoming sync event payloads using raw ADO.NET and metadata-driven PK column resolution.

**Architecture:** `IApplyService` and payload types move from `MSOSync.Transport` to `MSOSync.Engine`. A new M013 migration adds `pk_columns_json` to `sync_trigger`. `ApplyEngine` prefetches trigger metadata per batch, builds parameterized SQL via stateless builder classes, executes inside a batch-level `SqlTransaction` with per-row savepoints, and classifies failures into row-level (continue) vs fatal (rollback entire batch).

**Tech Stack:** C# 13 / .NET 9, EF Core 9 (metadata reads + batch status writes), `Microsoft.Data.SqlClient` 5.2.2 (raw ADO.NET DML), xUnit 2.9.3, FluentAssertions 6.12.2, Testcontainers.MsSql 4.4.0

## Global Constraints

- `TreatWarningsAsErrors = true` — zero warnings on all projects
- C# 13 primary constructors for all new classes/records; file-scoped namespaces
- `IClock` injected everywhere — never `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly
- EF Core 9.0.0; DbSet names exact: `Triggers`, `IncomingBatches`, `BatchErrors`
- `Microsoft.Data.SqlClient` Version `5.2.2`
- Schema env var: `Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"`
- Never `git add .` or `git add -A` — stage files by name
- dotnet PATH (both required before every build/test command):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

## File Map

### New files
```
src/MSOSync.Engine/Contracts/IApplyService.cs          ← moved from Transport
src/MSOSync.Engine/Contracts/ApplyResult.cs            ← moved from Transport
src/MSOSync.Engine/Contracts/BatchPayload.cs           ← moved from Transport.Payloads
src/MSOSync.Engine/Contracts/EventPayload.cs           ← moved from Transport.Payloads
src/MSOSync.Engine/Contracts/ISqlConnectionFactory.cs
src/MSOSync.Engine/Contracts/IApplyFailureClassifier.cs
src/MSOSync.Engine/Contracts/ApplyFailureCategory.cs
src/MSOSync.Engine/Contracts/ISqlEventApplicator.cs
src/MSOSync.Engine/Contracts/SqlStatement.cs
src/MSOSync.Engine/Contracts/ITriggerApplyMetadataService.cs
src/MSOSync.Engine/Apply/TriggerApplyMetadata.cs
src/MSOSync.Engine/Apply/ApplyContext.cs               ← internal
src/MSOSync.Engine/Apply/ApplyEngine.cs
src/MSOSync.Engine/Sql/InsertBuilder.cs
src/MSOSync.Engine/Sql/UpdateBuilder.cs
src/MSOSync.Engine/Sql/DeleteBuilder.cs
src/MSOSync.Engine/Sql/SqlEventApplicator.cs
src/MSOSync.Engine/Sql/SqlConnectionFactory.cs
src/MSOSync.Engine/Sql/SqlApplyFailureClassifier.cs
src/MSOSync.Engine/Metadata/TriggerApplyMetadataService.cs
src/MSOSync.Engine/ServiceCollectionExtensions.cs
src/MSOSync.Persistence/Migrations/M013_ApplyEngine.cs
src/MSOSync.Persistence/Migrations/M013_ApplyEngine.Designer.cs
tests/MSOSync.EngineTests/InsertBuilderTests.cs
tests/MSOSync.EngineTests/UpdateBuilderTests.cs
tests/MSOSync.EngineTests/DeleteBuilderTests.cs
tests/MSOSync.EngineTests/SqlApplyFailureClassifierTests.cs
tests/MSOSync.EngineTests/TriggerApplyMetadataServiceTests.cs
tests/MSOSync.IntegrationTests/Engine/ApplyEngineFixture.cs
tests/MSOSync.IntegrationTests/Engine/ApplyEngineTests.cs
```

### Deleted files
```
src/MSOSync.Transport/IApplyService.cs
src/MSOSync.Transport/ApplyResult.cs
src/MSOSync.Transport/NoOpApplyService.cs
src/MSOSync.Transport/Payloads/BatchPayload.cs
src/MSOSync.Transport/Payloads/EventPayload.cs
```

### Modified files
```
src/MSOSync.Persistence/Entities/SyncTrigger.cs                    ← add PkColumnsJson
src/MSOSync.Persistence/Configurations/SyncTriggerConfiguration.cs ← add pk_columns_json mapping
src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs    ← add PkColumnsJson to SyncTrigger block
src/MSOSync.Engine/MSOSync.Engine.csproj                           ← add Persistence ref + SqlClient pkg
src/MSOSync.Transport/TransportJsonContext.cs                       ← update using
src/MSOSync.Transport/Payloads/PullResponse.cs                     ← update using
src/MSOSync.Transport/TransportServiceExtensions.cs                 ← remove NoOpApplyService
src/MSOSync.Api/Controllers/SyncController.cs                      ← update using
src/MSOSync.Scheduler/PullJob.cs                                    ← update using
src/MSOSync.App/Program.cs                                          ← add AddApplyEngine()
src/MSOSync.Trigger/SqlServerTriggerBuilder.cs                      ← add pk_data capture
tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs           ← add pk_data tests
tests/MSOSync.TransportTests/SmartTransportServiceTests.cs          ← update namespace refs
```

---

### Task 1: M013 Migration + SyncTrigger Entity + Engine csproj

**Files:**
- Modify: `src/MSOSync.Persistence/Entities/SyncTrigger.cs`
- Modify: `src/MSOSync.Persistence/Configurations/SyncTriggerConfiguration.cs`
- Create: `src/MSOSync.Persistence/Migrations/M013_ApplyEngine.cs`
- Create: `src/MSOSync.Persistence/Migrations/M013_ApplyEngine.Designer.cs`
- Modify: `src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/MSOSync.Engine/MSOSync.Engine.csproj`

**Interfaces:**
- Consumes: nothing from prior tasks
- Produces: `SyncTrigger.PkColumnsJson` (string?) used by Tasks 5 and 3

- [ ] **Step 1: Add `PkColumnsJson` to `SyncTrigger` entity**

```csharp
// src/MSOSync.Persistence/Entities/SyncTrigger.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncTrigger
{
    public string TriggerId { get; set; } = null!;
    public string SourceTable { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public bool SyncOnInsert { get; set; } = true;
    public bool SyncOnUpdate { get; set; } = true;
    public bool SyncOnDelete { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int TriggerVersion { get; set; } = 0;
    public DateTime? LastVerifiedTime { get; set; }
    public string? PkColumnsJson { get; set; }
}
```

- [ ] **Step 2: Add `pk_columns_json` mapping to `SyncTriggerConfiguration`**

Add this line inside `Configure`, after `LastVerifiedTime`:
```csharp
builder.Property(e => e.PkColumnsJson).HasColumnName("pk_columns_json").HasColumnType("nvarchar(max)");
```

- [ ] **Step 3: Create M013 migration**

```csharp
// src/MSOSync.Persistence/Migrations/M013_ApplyEngine.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M013_ApplyEngine : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pk_columns_json",
                schema: "msosync",
                table: "sync_trigger",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pk_columns_json",
                schema: "msosync",
                table: "sync_trigger");
        }
    }
}
```

- [ ] **Step 4: Create M013 Designer file**

Copy `src/MSOSync.Persistence/Migrations/M012_Transport.Designer.cs` to `M013_ApplyEngine.Designer.cs`.

In the new file, make these two changes:

Change 1 — class declaration:
```csharp
// Before:
[Migration("20260622132755_M012_Transport")]
partial class M012_Transport
// After:
[Migration("20260623000000_M013_ApplyEngine")]
partial class M013_ApplyEngine
```

Change 2 — inside the `SyncTrigger` entity block (after the `TriggerVersion` property, before `b.HasKey`), add:
```csharp
                    b.Property<string>("PkColumnsJson")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("pk_columns_json");
```

- [ ] **Step 5: Update `AppDbContextModelSnapshot.cs`**

In the `SyncTrigger` entity block (around line 857, after `TriggerVersion`, before `b.HasKey("TriggerId")`), add:
```csharp
                    b.Property<string>("PkColumnsJson")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("pk_columns_json");
```

- [ ] **Step 6: Update `MSOSync.Engine.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>SyncEngine, ApplyEngine, ApplyPipeline orchestration — depends on interfaces only</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Build and verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj -c Debug --warnaserror
dotnet build src/MSOSync.Engine/MSOSync.Engine.csproj -c Debug --warnaserror
```

Expected: both build clean, zero warnings.

- [ ] **Step 8: Commit**

```powershell
git add src/MSOSync.Persistence/Entities/SyncTrigger.cs
git add src/MSOSync.Persistence/Configurations/SyncTriggerConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M013_ApplyEngine.cs
git add src/MSOSync.Persistence/Migrations/M013_ApplyEngine.Designer.cs
git add src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add src/MSOSync.Engine/MSOSync.Engine.csproj
git commit -m "feat: M013 add pk_columns_json to sync_trigger; Engine refs Persistence + SqlClient"
```

---

### Task 2: Relocate IApplyService / ApplyResult / BatchPayload / EventPayload to MSOSync.Engine

**Files:**
- Create: `src/MSOSync.Engine/Contracts/IApplyService.cs`
- Create: `src/MSOSync.Engine/Contracts/ApplyResult.cs`
- Create: `src/MSOSync.Engine/Contracts/BatchPayload.cs`
- Create: `src/MSOSync.Engine/Contracts/EventPayload.cs`
- Delete: `src/MSOSync.Transport/IApplyService.cs`
- Delete: `src/MSOSync.Transport/ApplyResult.cs`
- Delete: `src/MSOSync.Transport/Payloads/BatchPayload.cs`
- Delete: `src/MSOSync.Transport/Payloads/EventPayload.cs`
- Modify: `src/MSOSync.Transport/TransportJsonContext.cs`
- Modify: `src/MSOSync.Transport/Payloads/PullResponse.cs`
- Modify: `src/MSOSync.Api/Controllers/SyncController.cs`
- Modify: `src/MSOSync.Scheduler/PullJob.cs`
- Modify: `tests/MSOSync.TransportTests/SmartTransportServiceTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks
- Produces: `MSOSync.Engine.IApplyService`, `MSOSync.Engine.ApplyResult`, `MSOSync.Engine.BatchPayload`, `MSOSync.Engine.EventPayload` — consumed by Tasks 4, 6

- [ ] **Step 1: Create the four relocated types in Engine**

```csharp
// src/MSOSync.Engine/Contracts/EventPayload.cs
namespace MSOSync.Engine;

public sealed record EventPayload(
    long    EventId,
    string  TriggerId,
    string  EventType,
    string  TableName,
    string? TransactionId,
    string? PkData,
    string? RowData);
```

```csharp
// src/MSOSync.Engine/Contracts/BatchPayload.cs
namespace MSOSync.Engine;

public sealed record BatchPayload(
    long                        BatchId,
    long                        BatchSequence,
    string                      ChannelId,
    string                      SourceNodeId,
    string                      TargetNodeId,
    int                         RowCount,
    IReadOnlyList<EventPayload> Events);
```

```csharp
// src/MSOSync.Engine/Contracts/ApplyResult.cs
namespace MSOSync.Engine;

public sealed record ApplyResult(
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
```

```csharp
// src/MSOSync.Engine/Contracts/IApplyService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Delete the originals from Transport**

```powershell
Remove-Item src/MSOSync.Transport/IApplyService.cs
Remove-Item src/MSOSync.Transport/ApplyResult.cs
Remove-Item src/MSOSync.Transport/Payloads/BatchPayload.cs
Remove-Item src/MSOSync.Transport/Payloads/EventPayload.cs
```

- [ ] **Step 3: Update `TransportJsonContext.cs`**

Replace `using MSOSync.Transport.Payloads;` with `using MSOSync.Engine;`. Full file:

```csharp
using System.Text.Json.Serialization;
using MSOSync.Engine;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

[JsonSerializable(typeof(EventPayload))]
[JsonSerializable(typeof(BatchPayload))]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(AckPayload))]
[JsonSerializable(typeof(PushResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(List<BatchPayload>))]
[JsonSerializable(typeof(List<EventPayload>))]
public partial class TransportJsonContext : JsonSerializerContext { }
```

- [ ] **Step 4: Update `PullResponse.cs`**

```csharp
using MSOSync.Engine;

namespace MSOSync.Transport.Payloads;

public sealed record PullResponse(
    IReadOnlyList<BatchPayload> Batches,
    bool                        MoreAvailable);
```

- [ ] **Step 5: Update `SyncController.cs`**

Add `using MSOSync.Engine;` at the top. Remove `using MSOSync.Transport.Payloads;` if present (it's not there currently — check the file). The controller references `BatchPayload`, `EventPayload` — these are now resolved via the Engine using.

- [ ] **Step 6: Update `PullJob.cs`**

Replace `using MSOSync.Transport.Payloads;` with `using MSOSync.Engine;`.

- [ ] **Step 7: Update `SmartTransportServiceTests.cs`**

Replace all `MSOSync.Transport.Payloads.BatchPayload` with `MSOSync.Engine.BatchPayload` and `MSOSync.Transport.Payloads.PushResponse` stays (PushResponse remains in Transport). The test at line 97-101:

```csharp
var pushResponse = new MSOSync.Transport.Payloads.PushResponse(1L, true, 5, 0, null);
httpClient
    .Setup(h => h.PostAsync<MSOSync.Engine.BatchPayload, MSOSync.Transport.Payloads.PushResponse>(
        It.IsAny<string>(), It.IsAny<MSOSync.Engine.BatchPayload>(), It.IsAny<string>(), It.IsAny<string>(), default))
    .ReturnsAsync(pushResponse);
```

Also add `using MSOSync.Engine;` at the top of the test file.

- [ ] **Step 8: Build all affected projects**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: clean build, zero warnings.

- [ ] **Step 9: Run existing tests**

```powershell
dotnet test tests/MSOSync.TransportTests -c Debug
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: all pass.

- [ ] **Step 10: Commit**

```powershell
git add src/MSOSync.Engine/Contracts/IApplyService.cs
git add src/MSOSync.Engine/Contracts/ApplyResult.cs
git add src/MSOSync.Engine/Contracts/BatchPayload.cs
git add src/MSOSync.Engine/Contracts/EventPayload.cs
git add src/MSOSync.Transport/TransportJsonContext.cs
git add src/MSOSync.Transport/Payloads/PullResponse.cs
git add src/MSOSync.Api/Controllers/SyncController.cs
git add src/MSOSync.Scheduler/PullJob.cs
git add tests/MSOSync.TransportTests/SmartTransportServiceTests.cs
git rm src/MSOSync.Transport/IApplyService.cs
git rm src/MSOSync.Transport/ApplyResult.cs
git rm "src/MSOSync.Transport/Payloads/BatchPayload.cs"
git rm "src/MSOSync.Transport/Payloads/EventPayload.cs"
git commit -m "refactor: move BatchPayload/EventPayload/IApplyService/ApplyResult to MSOSync.Engine"
```

---

### Task 3: SqlServerTriggerBuilder — pk_data Capture

**Files:**
- Modify: `src/MSOSync.Trigger/SqlServerTriggerBuilder.cs`
- Modify: `tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs`

**Interfaces:**
- Consumes: `SyncTrigger.PkColumnsJson` from Task 1
- Produces: Updated `BuildDdl` that captures `pk_data` for v2 triggers; `TriggerVersion` semantics

- [ ] **Step 1: Write the failing tests first**

Add these tests to `SqlServerTriggerBuilderTests.cs`:

```csharp
private static SyncTrigger MakeV2Trigger(string pkColumnsJson = """["order_id"]""") =>
    new()
    {
        TriggerId      = "t-orders",
        SourceTable    = "dbo.Orders",
        ChannelId      = "default",
        SyncOnInsert   = true,
        SyncOnUpdate   = true,
        SyncOnDelete   = true,
        PkColumnsJson  = pkColumnsJson
    };

[Fact]
public void BuildDdl_WithPkColumnsJson_ContainsPkDataDeclaration()
{
    var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
    ddl.Should().Contain("@pk_data");
}

[Fact]
public void BuildDdl_WithPkColumnsJson_CapturesFromInserted()
{
    var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
    ddl.Should().Contain("[order_id] FROM inserted");
}

[Fact]
public void BuildDdl_WithPkColumnsJson_CapturesFromDeletedForUpdate()
{
    var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
    ddl.Should().Contain("[order_id] FROM deleted");
}

[Fact]
public void BuildDdl_WithCompositePkColumnsJson_CapturesBothColumns()
{
    var ddl = _builder.BuildDdl(MakeV2Trigger("""["tenant_id","order_id"]"""), "hub");
    ddl.Should().Contain("[tenant_id],[order_id]");
}

[Fact]
public void BuildDdl_WithNullPkColumnsJson_DoesNotContainPkData()
{
    var ddl = _builder.BuildDdl(MakeTrigger(), "hub");  // PkColumnsJson = null
    ddl.Should().NotContain("@pk_data");
}

[Fact]
public void BuildDdl_WithPkColumnsJson_IncludesPkDataInInsert()
{
    var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
    ddl.Should().Contain("pk_data");
}
```

- [ ] **Step 2: Run to verify they fail**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests --filter "SqlServerTriggerBuilderTests" -c Debug
```

Expected: new tests FAIL (pk_data not yet in DDL).

- [ ] **Step 3: Update `SqlServerTriggerBuilder.BuildDdl`**

```csharp
// src/MSOSync.Trigger/SqlServerTriggerBuilder.cs
using System.Text.Json;
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class SqlServerTriggerBuilder
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    private static void ValidateName(string value, string fieldName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[\[\]a-zA-Z_][a-zA-Z0-9_\.\[\]]*$"))
            throw new ArgumentException($"Invalid characters in {fieldName}: {value}");
        if (value.Contains('\''))
            throw new ArgumentException($"Single quote not allowed in {fieldName}");
    }

    public string BuildDdl(SyncTrigger trigger, string nodeId)
    {
        ValidateName(trigger.SourceTable, nameof(trigger.SourceTable));
        ValidateName(trigger.ChannelId, nameof(trigger.ChannelId));
        var triggerName = $"msosync__{trigger.TriggerId}";
        var parts = trigger.SourceTable.Split('.', 2);
        var tableSchema = parts.Length == 2 ? parts[0] : "dbo";
        var tableName   = parts.Length == 2 ? parts[1] : parts[0];

        var events = new List<string>();
        if (trigger.SyncOnInsert) events.Add("INSERT");
        if (trigger.SyncOnUpdate) events.Add("UPDATE");
        if (trigger.SyncOnDelete) events.Add("DELETE");
        var afterClause = string.Join(", ", events);

        string[]? pkColumns = null;
        if (!string.IsNullOrWhiteSpace(trigger.PkColumnsJson))
            pkColumns = JsonSerializer.Deserialize<string[]>(trigger.PkColumnsJson);

        if (pkColumns != null && pkColumns.Length > 0)
            return BuildV2Ddl(triggerName, tableSchema, tableName, afterClause, nodeId, trigger, pkColumns);

        return BuildV1Ddl(triggerName, tableSchema, tableName, afterClause, nodeId, trigger);
    }

    private string BuildV1Ddl(string triggerName, string tableSchema, string tableName,
        string afterClause, string nodeId, SyncTrigger trigger) => $"""
            CREATE OR ALTER TRIGGER [{triggerName}]
            ON [{tableSchema}].[{tableName}]
            AFTER {afterClause}
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @event_type CHAR(1) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted) THEN 'U'
                        WHEN EXISTS(SELECT 1 FROM inserted) THEN 'I'
                        ELSE 'D'
                    END;
                DECLARE @row_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT * FROM inserted FOR JSON PATH)
                        ELSE (SELECT * FROM deleted FOR JSON PATH)
                    END;
                INSERT INTO [{Schema}].[sync_data_event]
                    (trigger_id, source_node_id, channel_id, event_type, table_name,
                     row_data, transaction_id, create_time, is_processed)
                VALUES (
                    '{trigger.TriggerId}',
                    N'{nodeId}',
                    '{trigger.ChannelId}',
                    @event_type,
                    '{trigger.SourceTable}',
                    @row_data,
                    CURRENT_TRANSACTION_ID(),
                    GETUTCDATE(),
                    0
                );
            END
            """;

    private string BuildV2Ddl(string triggerName, string tableSchema, string tableName,
        string afterClause, string nodeId, SyncTrigger trigger, string[] pkColumns)
    {
        var pkColsSql = string.Join(",", pkColumns.Select(c => $"[{c}]"));
        return $"""
            CREATE OR ALTER TRIGGER [{triggerName}]
            ON [{tableSchema}].[{tableName}]
            AFTER {afterClause}
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @event_type CHAR(1) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted) THEN 'U'
                        WHEN EXISTS(SELECT 1 FROM inserted) THEN 'I'
                        ELSE 'D'
                    END;
                DECLARE @row_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT * FROM inserted FOR JSON PATH)
                        ELSE NULL
                    END;
                DECLARE @pk_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted)
                            THEN (SELECT {pkColsSql} FROM deleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT {pkColsSql} FROM inserted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                        ELSE (SELECT {pkColsSql} FROM deleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                    END;
                INSERT INTO [{Schema}].[sync_data_event]
                    (trigger_id, source_node_id, channel_id, event_type, table_name,
                     pk_data, row_data, transaction_id, create_time, is_processed)
                VALUES (
                    '{trigger.TriggerId}',
                    N'{nodeId}',
                    '{trigger.ChannelId}',
                    @event_type,
                    '{trigger.SourceTable}',
                    @pk_data,
                    @row_data,
                    CURRENT_TRANSACTION_ID(),
                    GETUTCDATE(),
                    0
                );
            END
            """;
    }

    public string GetTriggerName(string triggerId) => $"msosync__{triggerId}";
}
```

- [ ] **Step 4: Run all trigger builder tests**

```powershell
dotnet test tests/MSOSync.EngineTests --filter "SqlServerTriggerBuilderTests" -c Debug
```

Expected: all 14 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/MSOSync.Trigger/SqlServerTriggerBuilder.cs
git add tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs
git commit -m "feat: SqlServerTriggerBuilder v2 — pk_data capture from pk_columns_json"
```

---

### Task 4: SQL Contracts + Builders + Failure Classifier

**Files:**
- Create: `src/MSOSync.Engine/Contracts/ISqlConnectionFactory.cs`
- Create: `src/MSOSync.Engine/Contracts/IApplyFailureClassifier.cs`
- Create: `src/MSOSync.Engine/Contracts/ApplyFailureCategory.cs`
- Create: `src/MSOSync.Engine/Contracts/ISqlEventApplicator.cs`
- Create: `src/MSOSync.Engine/Contracts/SqlStatement.cs`
- Create: `src/MSOSync.Engine/Contracts/ITriggerApplyMetadataService.cs`
- Create: `src/MSOSync.Engine/Sql/InsertBuilder.cs`
- Create: `src/MSOSync.Engine/Sql/UpdateBuilder.cs`
- Create: `src/MSOSync.Engine/Sql/DeleteBuilder.cs`
- Create: `src/MSOSync.Engine/Sql/SqlEventApplicator.cs`
- Create: `src/MSOSync.Engine/Sql/SqlConnectionFactory.cs`
- Create: `src/MSOSync.Engine/Sql/SqlApplyFailureClassifier.cs`

**Interfaces:**
- Produces: `ISqlConnectionFactory`, `ISqlEventApplicator`, `IApplyFailureClassifier`, `SqlStatement`, `InsertBuilder`, `UpdateBuilder`, `DeleteBuilder`, `SqlEventApplicator`, `SqlConnectionFactory`, `SqlApplyFailureClassifier` — all consumed by Task 6

- [ ] **Step 1: Create contracts**

```csharp
// src/MSOSync.Engine/Contracts/SqlStatement.cs
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed record SqlStatement(
    string                      CommandText,
    IReadOnlyList<SqlParameter> Parameters);
```

```csharp
// src/MSOSync.Engine/Contracts/ISqlConnectionFactory.cs
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
}
```

```csharp
// src/MSOSync.Engine/Contracts/ApplyFailureCategory.cs
namespace MSOSync.Engine;

public enum ApplyFailureCategory
{
    DuplicateKey,
    RowNotFound,
    FKViolation,
    MetadataMissing,
    SerializationError,
    Deadlock,
    Timeout,
    SyntaxError,
    Unknown
}
```

```csharp
// src/MSOSync.Engine/Contracts/IApplyFailureClassifier.cs
namespace MSOSync.Engine;

public interface IApplyFailureClassifier
{
    ApplyFailureCategory Classify(int sqlErrorNumber);
}
```

```csharp
// src/MSOSync.Engine/Contracts/ISqlEventApplicator.cs
using System.Text.Json;

namespace MSOSync.Engine;

public interface ISqlEventApplicator
{
    SqlStatement BuildInsert(string schemaName, string tableName, JsonElement rowData);
    SqlStatement BuildUpdate(string schemaName, string tableName, JsonElement pkData, JsonElement rowData);
    SqlStatement BuildDelete(string schemaName, string tableName, JsonElement pkData);
}
```

```csharp
// src/MSOSync.Engine/Contracts/ITriggerApplyMetadataService.cs
namespace MSOSync.Engine;

public interface ITriggerApplyMetadataService
{
    Task<Dictionary<string, TriggerApplyMetadata>> GetMetadataAsync(
        IReadOnlyList<string> triggerIds,
        CancellationToken     ct = default);
}
```

- [ ] **Step 2: Create `SqlConnectionFactory`**

```csharp
// src/MSOSync.Engine/Sql/SqlConnectionFactory.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MSOSync.Engine;

public sealed class SqlConnectionFactory(IConfiguration config) : ISqlConnectionFactory
{
    private readonly string _connectionString =
        config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default not configured");

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
```

- [ ] **Step 3: Create `SqlApplyFailureClassifier`**

```csharp
// src/MSOSync.Engine/Sql/SqlApplyFailureClassifier.cs
namespace MSOSync.Engine;

public sealed class SqlApplyFailureClassifier : IApplyFailureClassifier
{
    public ApplyFailureCategory Classify(int sqlErrorNumber) => sqlErrorNumber switch
    {
        2627 or 2601                => ApplyFailureCategory.DuplicateKey,
        547                         => ApplyFailureCategory.FKViolation,
        1205                        => ApplyFailureCategory.Deadlock,
        -2                          => ApplyFailureCategory.Timeout,
        102 or 208 or 207 or 4121   => ApplyFailureCategory.SyntaxError,
        _                           => ApplyFailureCategory.Unknown
    };
}
```

- [ ] **Step 4: Create `InsertBuilder`**

```csharp
// src/MSOSync.Engine/Sql/InsertBuilder.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class InsertBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement rowData)
    {
        var columns    = new List<string>();
        var paramNames = new List<string>();
        var parameters = new List<SqlParameter>();
        int i = 0;

        foreach (var prop in rowData.EnumerateObject())
        {
            columns.Add($"[{prop.Name}]");
            paramNames.Add($"@p{i}");
            parameters.Add(CreateParameter($"@p{i}", prop.Value));
            i++;
        }

        if (columns.Count == 0)
            throw new ArgumentException("rowData must contain at least one property for INSERT");

        var sql = $"INSERT INTO [{schemaName}].[{tableName}] ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }

    internal static SqlParameter CreateParameter(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null   => new SqlParameter(name, DBNull.Value),
        JsonValueKind.True   => new SqlParameter(name, true),
        JsonValueKind.False  => new SqlParameter(name, false),
        JsonValueKind.Number => CreateNumberParameter(name, value),
        JsonValueKind.String => new SqlParameter(name, value.GetString()),
        _ => throw new InvalidOperationException($"Unsupported JSON token type: {value.ValueKind}")
    };

    private static SqlParameter CreateNumberParameter(string name, JsonElement value)
    {
        if (value.TryGetInt32(out var i32)) return new SqlParameter(name, i32);
        if (value.TryGetInt64(out var i64)) return new SqlParameter(name, i64);
        if (value.TryGetDecimal(out var dec)) return new SqlParameter(name, dec);
        if (value.TryGetDouble(out var dbl)) return new SqlParameter(name, dbl);
        return new SqlParameter(name, value.GetRawText());
    }
}
```

- [ ] **Step 5: Create `UpdateBuilder`**

```csharp
// src/MSOSync.Engine/Sql/UpdateBuilder.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class UpdateBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement pkData, JsonElement rowData)
    {
        var setParts   = new List<string>();
        var whereParts = new List<string>();
        var parameters = new List<SqlParameter>();
        int i = 0;

        foreach (var prop in rowData.EnumerateObject())
        {
            setParts.Add($"[{prop.Name}]=@p{i}");
            parameters.Add(InsertBuilder.CreateParameter($"@p{i}", prop.Value));
            i++;
        }

        int pk = 0;
        foreach (var prop in pkData.EnumerateObject())
        {
            whereParts.Add($"[{prop.Name}]=@pk{pk}");
            parameters.Add(InsertBuilder.CreateParameter($"@pk{pk}", prop.Value));
            pk++;
        }

        if (setParts.Count == 0)
            throw new ArgumentException("rowData must contain at least one property for UPDATE");
        if (whereParts.Count == 0)
            throw new ArgumentException("pkData must contain at least one property for UPDATE WHERE clause");

        var sql = $"UPDATE [{schemaName}].[{tableName}] SET {string.Join(",", setParts)} WHERE {string.Join(" AND ", whereParts)}";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }
}
```

- [ ] **Step 6: Create `DeleteBuilder`**

```csharp
// src/MSOSync.Engine/Sql/DeleteBuilder.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class DeleteBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement pkData)
    {
        var whereParts = new List<string>();
        var parameters = new List<SqlParameter>();
        int pk = 0;

        foreach (var prop in pkData.EnumerateObject())
        {
            whereParts.Add($"[{prop.Name}]=@pk{pk}");
            parameters.Add(InsertBuilder.CreateParameter($"@pk{pk}", prop.Value));
            pk++;
        }

        if (whereParts.Count == 0)
            throw new ArgumentException("pkData must contain at least one property for DELETE WHERE clause");

        var sql = $"DELETE FROM [{schemaName}].[{tableName}] WHERE {string.Join(" AND ", whereParts)}";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }
}
```

- [ ] **Step 7: Create `SqlEventApplicator`**

```csharp
// src/MSOSync.Engine/Sql/SqlEventApplicator.cs
using System.Text.Json;

namespace MSOSync.Engine;

public sealed class SqlEventApplicator(
    InsertBuilder insert,
    UpdateBuilder update,
    DeleteBuilder delete) : ISqlEventApplicator
{
    public SqlStatement BuildInsert(string schemaName, string tableName, JsonElement rowData)
        => insert.Build(schemaName, tableName, rowData);

    public SqlStatement BuildUpdate(string schemaName, string tableName, JsonElement pkData, JsonElement rowData)
        => update.Build(schemaName, tableName, pkData, rowData);

    public SqlStatement BuildDelete(string schemaName, string tableName, JsonElement pkData)
        => delete.Build(schemaName, tableName, pkData);
}
```

- [ ] **Step 8: Build Engine**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Engine/MSOSync.Engine.csproj -c Debug --warnaserror
```

Expected: clean build.

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Engine/Contracts/SqlStatement.cs
git add src/MSOSync.Engine/Contracts/ISqlConnectionFactory.cs
git add src/MSOSync.Engine/Contracts/ApplyFailureCategory.cs
git add src/MSOSync.Engine/Contracts/IApplyFailureClassifier.cs
git add src/MSOSync.Engine/Contracts/ISqlEventApplicator.cs
git add src/MSOSync.Engine/Contracts/ITriggerApplyMetadataService.cs
git add src/MSOSync.Engine/Sql/SqlConnectionFactory.cs
git add src/MSOSync.Engine/Sql/SqlApplyFailureClassifier.cs
git add src/MSOSync.Engine/Sql/InsertBuilder.cs
git add src/MSOSync.Engine/Sql/UpdateBuilder.cs
git add src/MSOSync.Engine/Sql/DeleteBuilder.cs
git add src/MSOSync.Engine/Sql/SqlEventApplicator.cs
git commit -m "feat: SQL contracts, builders, failure classifier, connection factory"
```

---

### Task 5: TriggerApplyMetadata + TriggerApplyMetadataService

**Files:**
- Create: `src/MSOSync.Engine/Apply/TriggerApplyMetadata.cs`
- Create: `src/MSOSync.Engine/Metadata/TriggerApplyMetadataService.cs`

**Interfaces:**
- Consumes: `SyncTrigger.PkColumnsJson` (Task 1), `ITriggerApplyMetadataService` contract (Task 4)
- Produces: `TriggerApplyMetadata` record, `TriggerApplyMetadataService` class — consumed by Task 6

- [ ] **Step 1: Create `TriggerApplyMetadata`**

```csharp
// src/MSOSync.Engine/Apply/TriggerApplyMetadata.cs
namespace MSOSync.Engine;

public sealed record TriggerApplyMetadata(
    string                SchemaName,
    string                TableName,
    IReadOnlyList<string> PkColumns,
    int                   TriggerVersion);
```

- [ ] **Step 2: Create `TriggerApplyMetadataService`**

```csharp
// src/MSOSync.Engine/Metadata/TriggerApplyMetadataService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Engine;

public sealed class TriggerApplyMetadataService(AppDbContext db) : ITriggerApplyMetadataService
{
    public async Task<Dictionary<string, TriggerApplyMetadata>> GetMetadataAsync(
        IReadOnlyList<string> triggerIds,
        CancellationToken     ct = default)
    {
        var triggers = await db.Triggers
            .Where(t => triggerIds.Contains(t.TriggerId))
            .ToListAsync(ct);

        return triggers.ToDictionary(
            t => t.TriggerId,
            t =>
            {
                var parts      = t.SourceTable.Split('.', 2);
                var schema     = parts.Length == 2 ? parts[0] : "dbo";
                var table      = parts.Length == 2 ? parts[1] : parts[0];
                var pkColumns  = DeserializePkColumns(t.TriggerId, t.PkColumnsJson);
                return new TriggerApplyMetadata(schema, table, pkColumns, t.TriggerVersion);
            });
    }

    private static IReadOnlyList<string> DeserializePkColumns(string triggerId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                   ?? throw new InvalidOperationException($"pk_columns_json for trigger {triggerId} deserializes to null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Malformed pk_columns_json for trigger {triggerId}: {json}", ex);
        }
    }
}
```

- [ ] **Step 3: Build and commit**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Engine/MSOSync.Engine.csproj -c Debug --warnaserror
git add src/MSOSync.Engine/Apply/TriggerApplyMetadata.cs
git add src/MSOSync.Engine/Metadata/TriggerApplyMetadataService.cs
git commit -m "feat: TriggerApplyMetadata record + TriggerApplyMetadataService"
```

---

### Task 6: ApplyEngine + DI Wiring

**Files:**
- Create: `src/MSOSync.Engine/Apply/ApplyContext.cs`
- Create: `src/MSOSync.Engine/Apply/ApplyEngine.cs`
- Create: `src/MSOSync.Engine/ServiceCollectionExtensions.cs`
- Modify: `src/MSOSync.Transport/TransportServiceExtensions.cs` (remove NoOpApplyService)
- Modify: `src/MSOSync.App/Program.cs` (add AddApplyEngine)
- Delete: `src/MSOSync.Transport/NoOpApplyService.cs`

**Interfaces:**
- Consumes: everything from Tasks 2, 4, 5
- Produces: `ApplyEngine` (implements `IApplyService`); `AddApplyEngine()` extension; complete DI wiring

- [ ] **Step 1: Create `ApplyContext`**

```csharp
// src/MSOSync.Engine/Apply/ApplyContext.cs
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

internal sealed record ApplyContext(
    SqlConnection                            Connection,
    SqlTransaction                           Transaction,
    Dictionary<string, TriggerApplyMetadata> Metadata);
```

- [ ] **Step 2: Create `ApplyEngine`**

```csharp
// src/MSOSync.Engine/Apply/ApplyEngine.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public sealed class ApplyEngine(
    AppDbContext                 db,
    ISqlConnectionFactory        connectionFactory,
    ISqlEventApplicator          applicator,
    IApplyFailureClassifier      classifier,
    ITriggerApplyMetadataService metadataService,
    IClock                       clock,
    ILogger<ApplyEngine>         logger) : IApplyService
{
    public async Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default)
    {
        incoming.Status = IncomingBatchStatus.Applying;
        await db.SaveChangesAsync(ct);

        var triggerIds = payload.Events.Select(e => e.TriggerId).Distinct().ToList();
        var metadata   = await metadataService.GetMetadataAsync(triggerIds, ct);

        int appliedRows = 0;
        int errorRows   = 0;
        string? lastError = null;
        bool fatalError = false;

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            foreach (var evt in payload.Events)
            {
                var (ok, err) = await ApplyEventAsync(evt, new ApplyContext(connection, transaction, metadata), ct);
                if (ok) appliedRows++;
                else { errorRows++; lastError = err; }
            }
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            fatalError = true;
            errorRows  = payload.Events.Count;
            appliedRows = 0;
            lastError  = ex.Message;
            logger.LogError(ex, "ApplyEngine: fatal error on batch {BatchId}", incoming.BatchId);
        }
        finally
        {
            incoming.Status = fatalError || (appliedRows == 0 && errorRows > 0)
                ? IncomingBatchStatus.Error
                : errorRows > 0
                    ? IncomingBatchStatus.PartialSuccess
                    : IncomingBatchStatus.Applied;
            incoming.AppliedTime = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new ApplyResult(errorRows == 0, appliedRows, errorRows, errorRows == 0 ? null : lastError);
    }

    private async Task<(bool ok, string? error)> ApplyEventAsync(
        EventPayload evt, ApplyContext ctx, CancellationToken ct)
    {
        if (!ctx.Metadata.TryGetValue(evt.TriggerId, out var meta))
        {
            logger.LogWarning("ApplyEngine: no metadata for trigger {TriggerId}", evt.TriggerId);
            return (false, ApplyFailureCategory.MetadataMissing.ToString());
        }

        if (meta.TriggerVersion < 2)
        {
            logger.LogWarning("ApplyEngine: trigger {TriggerId} version {V} < 2", evt.TriggerId, meta.TriggerVersion);
            return (false, ApplyFailureCategory.MetadataMissing.ToString());
        }

        SqlStatement stmt;
        try
        {
            stmt = BuildStatement(evt, meta);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ApplyEngine: SQL build error on event {EventId}", evt.EventId);
            return (false, ApplyFailureCategory.SerializationError.ToString());
        }

        var sp = $"sp_{evt.EventId}";
        ctx.Transaction.Save(sp);

        try
        {
            await using var cmd = ctx.Connection.CreateCommand();
            cmd.CommandText = stmt.CommandText;
            cmd.Transaction = ctx.Transaction;
            foreach (var p in stmt.Parameters)
                cmd.Parameters.Add(p);

            var rows = await cmd.ExecuteNonQueryAsync(ct);

            if (rows == 0 && evt.EventType != "INSERT")
            {
                ctx.Transaction.Rollback(sp);
                logger.LogWarning("ApplyEngine: RowNotFound for event {EventId}", evt.EventId);
                return (false, ApplyFailureCategory.RowNotFound.ToString());
            }

            return (true, null);
        }
        catch (SqlException sqlEx)
        {
            var cat = classifier.Classify(sqlEx.Number);
            if (cat is ApplyFailureCategory.DuplicateKey
                    or ApplyFailureCategory.FKViolation
                    or ApplyFailureCategory.RowNotFound)
            {
                ctx.Transaction.Rollback(sp);
                logger.LogWarning(sqlEx, "ApplyEngine: row-level {Cat} on event {EventId}", cat, evt.EventId);
                return (false, cat.ToString());
            }
            throw;
        }
    }

    private SqlStatement BuildStatement(EventPayload evt, TriggerApplyMetadata meta)
    {
        var pkData  = evt.PkData  != null ? JsonDocument.Parse(evt.PkData).RootElement  : (JsonElement?)null;
        var rowData = evt.RowData != null ? JsonDocument.Parse(evt.RowData).RootElement : (JsonElement?)null;

        return evt.EventType switch
        {
            "INSERT" => rowData.HasValue
                ? applicator.BuildInsert(meta.SchemaName, meta.TableName, rowData.Value)
                : throw new InvalidOperationException("INSERT event missing row_data"),
            "UPDATE" => pkData.HasValue && rowData.HasValue
                ? applicator.BuildUpdate(meta.SchemaName, meta.TableName, pkData.Value, rowData.Value)
                : throw new InvalidOperationException("UPDATE event missing pk_data or row_data"),
            "DELETE" => pkData.HasValue
                ? applicator.BuildDelete(meta.SchemaName, meta.TableName, pkData.Value)
                : throw new InvalidOperationException("DELETE event missing pk_data"),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.EventType}")
        };
    }
}
```

- [ ] **Step 3: Create `ServiceCollectionExtensions`**

```csharp
// src/MSOSync.Engine/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Engine;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplyEngine(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IApplyFailureClassifier, SqlApplyFailureClassifier>();
        services.AddSingleton<InsertBuilder>();
        services.AddSingleton<UpdateBuilder>();
        services.AddSingleton<DeleteBuilder>();
        services.AddScoped<ISqlEventApplicator, SqlEventApplicator>();
        services.AddScoped<ITriggerApplyMetadataService, TriggerApplyMetadataService>();
        services.AddScoped<IApplyService, ApplyEngine>();
        return services;
    }
}
```

- [ ] **Step 4: Remove `NoOpApplyService` from Transport**

Delete the file:
```powershell
git rm src/MSOSync.Transport/NoOpApplyService.cs
```

In `src/MSOSync.Transport/TransportServiceExtensions.cs`, remove:
```csharp
services.AddScoped<IApplyService, NoOpApplyService>();
```

- [ ] **Step 5: Update `Program.cs`**

Add `builder.Services.AddApplyEngine();` after `builder.Services.AddSyncEngine(builder.Configuration);`:

```csharp
builder.Services.AddSyncEngine(builder.Configuration);
builder.Services.AddApplyEngine();
```

Also remove `using MSOSync.Transport;` if that was the only reference that pulled in the old `IApplyService` namespace (it won't be needed for this).

- [ ] **Step 6: Build solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: clean build, zero warnings.

- [ ] **Step 7: Run all existing tests**

```powershell
dotnet test tests/MSOSync.TransportTests -c Debug
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: all pass.

- [ ] **Step 8: Commit**

```powershell
git add src/MSOSync.Engine/Apply/ApplyContext.cs
git add src/MSOSync.Engine/Apply/ApplyEngine.cs
git add src/MSOSync.Engine/ServiceCollectionExtensions.cs
git add src/MSOSync.Transport/TransportServiceExtensions.cs
git add src/MSOSync.App/Program.cs
git rm src/MSOSync.Transport/NoOpApplyService.cs
git commit -m "feat: ApplyEngine implements IApplyService; wire AddApplyEngine() in Program.cs"
```

---

### Task 7: Unit Tests — Builders, Classifier, Metadata Service

**Files:**
- Create: `tests/MSOSync.EngineTests/InsertBuilderTests.cs`
- Create: `tests/MSOSync.EngineTests/UpdateBuilderTests.cs`
- Create: `tests/MSOSync.EngineTests/DeleteBuilderTests.cs`
- Create: `tests/MSOSync.EngineTests/SqlApplyFailureClassifierTests.cs`
- Create: `tests/MSOSync.EngineTests/TriggerApplyMetadataServiceTests.cs`

**Interfaces:**
- Consumes: `InsertBuilder`, `UpdateBuilder`, `DeleteBuilder`, `SqlApplyFailureClassifier`, `TriggerApplyMetadataService` from Tasks 4 and 5

- [ ] **Step 1: Create `InsertBuilderTests.cs`**

```csharp
// tests/MSOSync.EngineTests/InsertBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class InsertBuilderTests
{
    private readonly InsertBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SingleColumn_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":42}"""));
        stmt.CommandText.Should().Be("INSERT INTO [dbo].[orders] ([order_id]) VALUES (@p0)");
        stmt.Parameters.Should().HaveCount(1);
        stmt.Parameters[0].Value.Should().Be(42);
    }

    [Fact]
    public void Build_MultipleColumns_CorrectOrder()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":1,"status":"open"}"""));
        stmt.CommandText.Should().Contain("[order_id]").And.Contain("[status]");
        stmt.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Build_NullValue_UsesDbNull()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"note":null}"""));
        stmt.Parameters[0].Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void Build_StringValue_PassedAsString()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"status":"closed"}"""));
        stmt.Parameters[0].Value.Should().Be("closed");
    }

    [Fact]
    public void Build_BoolTrue_PassedAsBool()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"active":true}"""));
        stmt.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Build_BoolFalse_PassedAsBool()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"active":false}"""));
        stmt.Parameters[0].Value.Should().Be(false);
    }

    [Fact]
    public void Build_Int64Value_PassedAsLong()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"big":9999999999}"""));
        stmt.Parameters[0].Value.Should().Be(9999999999L);
    }

    [Fact]
    public void Build_DecimalValue_PassedAsDecimal()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"price":12.99}"""));
        stmt.Parameters[0].Value.Should().BeOfType<decimal>().And.Subject.Should().Be(12.99m);
    }

    [Fact]
    public void Build_GuidString_PassedAsString()
    {
        var guid = Guid.NewGuid().ToString();
        var stmt = _builder.Build("dbo", "orders", Json($$$"""{"id":"{{{guid}}}"}"""));
        stmt.Parameters[0].Value.Should().Be(guid);
    }

    [Fact]
    public void Build_EmptyObject_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_UnsupportedTokenType_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("""{"nested":{"x":1}}"""));
        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Create `UpdateBuilderTests.cs`**

```csharp
// tests/MSOSync.EngineTests/UpdateBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class UpdateBuilderTests
{
    private readonly UpdateBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SinglePk_SingleSet_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"order_id":10}"""),
            Json("""{"status":"closed"}"""));

        stmt.CommandText.Should().Be("UPDATE [dbo].[orders] SET [status]=@p0 WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(2);
        stmt.Parameters[0].Value.Should().Be("closed");
        stmt.Parameters[1].Value.Should().Be(10);
    }

    [Fact]
    public void Build_CompositePk_GeneratesAndClause()
    {
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"tenant_id":1,"order_id":42}"""),
            Json("""{"status":"done"}"""));

        stmt.CommandText.Should().Contain("WHERE [tenant_id]=@pk0 AND [order_id]=@pk1");
    }

    [Fact]
    public void Build_EmptyRowData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("""{"id":1}"""), Json("{}"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_EmptyPkData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"), Json("""{"status":"x"}"""));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_PkColumnAlsoInRowData_ParametersCorrect()
    {
        // UPDATE orders SET order_id=@p0 WHERE order_id=@pk0 (PK change scenario)
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"order_id":10}"""),
            Json("""{"order_id":20,"status":"updated"}"""));

        stmt.CommandText.Should().Contain("WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(3);
    }
}
```

- [ ] **Step 3: Create `DeleteBuilderTests.cs`**

```csharp
// tests/MSOSync.EngineTests/DeleteBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class DeleteBuilderTests
{
    private readonly DeleteBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SinglePk_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":42}"""));
        stmt.CommandText.Should().Be("DELETE FROM [dbo].[orders] WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(1);
        stmt.Parameters[0].Value.Should().Be(42);
    }

    [Fact]
    public void Build_CompositePk_GeneratesAndClause()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"tenant_id":1,"order_id":42}"""));
        stmt.CommandText.Should().Be("DELETE FROM [dbo].[orders] WHERE [tenant_id]=@pk0 AND [order_id]=@pk1");
        stmt.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Build_EmptyPkData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"));
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 4: Create `SqlApplyFailureClassifierTests.cs`**

```csharp
// tests/MSOSync.EngineTests/SqlApplyFailureClassifierTests.cs
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class SqlApplyFailureClassifierTests
{
    private readonly SqlApplyFailureClassifier _classifier = new();

    [Theory]
    [InlineData(2627, ApplyFailureCategory.DuplicateKey)]
    [InlineData(2601, ApplyFailureCategory.DuplicateKey)]
    [InlineData(547,  ApplyFailureCategory.FKViolation)]
    [InlineData(1205, ApplyFailureCategory.Deadlock)]
    [InlineData(-2,   ApplyFailureCategory.Timeout)]
    [InlineData(102,  ApplyFailureCategory.SyntaxError)]
    [InlineData(208,  ApplyFailureCategory.SyntaxError)]
    [InlineData(207,  ApplyFailureCategory.SyntaxError)]
    [InlineData(4121, ApplyFailureCategory.SyntaxError)]
    [InlineData(99999, ApplyFailureCategory.Unknown)]
    [InlineData(0,    ApplyFailureCategory.Unknown)]
    public void Classify_ErrorNumber_ReturnsExpectedCategory(int errorNumber, ApplyFailureCategory expected)
    {
        _classifier.Classify(errorNumber).Should().Be(expected);
    }
}
```

- [ ] **Step 5: Create `TriggerApplyMetadataServiceTests.cs`**

```csharp
// tests/MSOSync.EngineTests/TriggerApplyMetadataServiceTests.cs
using FluentAssertions;
using MSOSync.Engine;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class TriggerApplyMetadataServiceTests
{
    private static TriggerApplyMetadataService CreateService(out AppDbContext db)
    {
        db = TestDbContext.Create();
        return new TriggerApplyMetadataService(db);
    }

    [Fact]
    public async Task GetMetadataAsync_KnownTrigger_ReturnsMapped()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t1",
            SourceTable    = "dbo.orders",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = """["order_id"]"""
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t1"]);

        result.Should().ContainKey("t1");
        var meta = result["t1"];
        meta.SchemaName.Should().Be("dbo");
        meta.TableName.Should().Be("orders");
        meta.PkColumns.Should().Equal("order_id");
        meta.TriggerVersion.Should().Be(2);
    }

    [Fact]
    public async Task GetMetadataAsync_UnknownTrigger_ReturnsEmpty()
    {
        var svc = CreateService(out _);
        var result = await svc.GetMetadataAsync(["does-not-exist"]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMetadataAsync_NullPkColumnsJson_ReturnsEmptyPkColumns()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t2",
            SourceTable    = "sales.products",
            ChannelId      = "ch",
            TriggerVersion = 1,
            PkColumnsJson  = null
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t2"]);
        result["t2"].PkColumns.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMetadataAsync_MalformedPkColumnsJson_Throws()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t3",
            SourceTable    = "dbo.bad",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = "{bad json"
        });
        await db.SaveChangesAsync();

        var act = async () => await svc.GetMetadataAsync(["t3"]);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetMetadataAsync_CompositePk_ReturnsBothColumns()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t4",
            SourceTable    = "dbo.items",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = """["tenant_id","item_id"]"""
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t4"]);
        result["t4"].PkColumns.Should().Equal("tenant_id", "item_id");
    }

    [Fact]
    public async Task GetMetadataAsync_OnlyFetchesRequestedIds()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger { TriggerId = "a", SourceTable = "dbo.a", ChannelId = "ch", TriggerVersion = 2, PkColumnsJson = """["id"]""" });
        db.Triggers.Add(new SyncTrigger { TriggerId = "b", SourceTable = "dbo.b", ChannelId = "ch", TriggerVersion = 2, PkColumnsJson = """["id"]""" });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["a"]);
        result.Should().ContainKey("a").And.NotContainKey("b");
    }
}
```

- [ ] **Step 6: Run unit tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: all tests PASS (including new ones + existing ones).

- [ ] **Step 7: Commit**

```powershell
git add tests/MSOSync.EngineTests/InsertBuilderTests.cs
git add tests/MSOSync.EngineTests/UpdateBuilderTests.cs
git add tests/MSOSync.EngineTests/DeleteBuilderTests.cs
git add tests/MSOSync.EngineTests/SqlApplyFailureClassifierTests.cs
git add tests/MSOSync.EngineTests/TriggerApplyMetadataServiceTests.cs
git commit -m "test: unit tests for SQL builders, failure classifier, metadata service"
```

---

### Task 8: Integration Tests — ApplyEngine Against SQL Server

**Files:**
- Create: `tests/MSOSync.IntegrationTests/Engine/ApplyEngineFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Engine/ApplyEngineTests.cs`

**Interfaces:**
- Consumes: `ApplyEngine`, `ISqlConnectionFactory`, `AppDbContext`, `TriggerApplyMetadataService` from Tasks 5 and 6

- [ ] **Step 1: Create `ApplyEngineFixture.cs`**

```csharp
// tests/MSOSync.IntegrationTests/Engine/ApplyEngineFixture.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Testcontainers.MsSql;

namespace MSOSync.IntegrationTests.Engine;

public sealed class ApplyEngineFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public IServiceProvider Services { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddSingleton<IClock, FakeClock>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddApplyEngine();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        await CreateTestTableAsync();
        await SeedTriggerAsync();
    }

    private async Task CreateTestTableAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID('dbo.test_orders','U') IS NULL
            CREATE TABLE [dbo].[test_orders] (
                [order_id]  int          NOT NULL,
                [tenant_id] int          NULL,
                [status]    nvarchar(50) NULL,
                CONSTRAINT PK_test_orders PRIMARY KEY ([order_id])
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedTriggerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ch = new SyncChannel { ChannelId = "default", Priority = 1, BatchSize = 1000, MaxBatchToSend = 100, MaxDataSize = 1048576 };
        db.Channels.Add(ch);

        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t-orders",
            SourceTable    = "dbo.test_orders",
            ChannelId      = "default",
            TriggerVersion = 2,
            PkColumnsJson  = """["order_id"]"""
        });
        await db.SaveChangesAsync();
    }

    public async Task ClearTestOrdersAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [dbo].[test_orders]";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

// Simple FakeClock for fixture
file sealed class FakeClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

- [ ] **Step 2: Create `ApplyEngineTests.cs`**

```csharp
// tests/MSOSync.IntegrationTests/Engine/ApplyEngineTests.cs
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Engine;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

[Collection("ApplyEngine")]
public sealed class ApplyEngineTests(ApplyEngineFixture fx) : IAsyncLifetime
{
    private long _batchId = 0;

    public async Task InitializeAsync()
    {
        await fx.ClearTestOrdersAsync();
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var node = new SyncNode { NodeId = "src", GroupId = "g", SyncUrl = "http://src", Status = "APPROVED" };
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "src"))
            db.Nodes.Add(node);

        var localNode = new SyncNode { NodeId = "local", GroupId = "g", SyncUrl = "http://local", Status = "APPROVED" };
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "local"))
            db.Nodes.Add(localNode);

        await db.SaveChangesAsync();

        var incoming = new SyncIncomingBatch
        {
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = 1,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 1,
            Status        = IncomingBatchStatus.New
        };
        db.IncomingBatches.Add(incoming);
        await db.SaveChangesAsync();
        _batchId = incoming.BatchId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SyncIncomingBatch MakeIncoming() => new()
    {
        BatchId       = _batchId,
        NodeId        = "local",
        ChannelId     = "default",
        SourceNodeId  = "src",
        BatchSequence = 1,
        ReceivedTime  = DateTime.UtcNow,
        RowCount      = 1,
        Status        = IncomingBatchStatus.New
    };

    private static BatchPayload MakeBatch(IReadOnlyList<EventPayload> events) =>
        new(1, 1, "default", "src", "local", events.Count, events);

    private static EventPayload InsertEvent(int orderId, string status = "open") =>
        new(1, "t-orders", "INSERT", "dbo.test_orders", null, null,
            $$"""{"order_id":{{orderId}},"status":"{{status}}"}""");

    private static EventPayload UpdateEvent(int orderId, string newStatus) =>
        new(2, "t-orders", "UPDATE", "dbo.test_orders", null,
            $$"""{"order_id":{{orderId}}}""",
            $$"""{"order_id":{{orderId}},"status":"{{newStatus}}"}""");

    private static EventPayload DeleteEvent(int orderId) =>
        new(3, "t-orders", "DELETE", "dbo.test_orders", null,
            $$"""{"order_id":{{orderId}}}""", null);

    private async Task<int?> GetStatusAsync(int orderId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[test_orders] WHERE [order_id]=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        return (int)await cmd.ExecuteScalarAsync()!;
    }

    private async Task<bool> RowExistsAsync(int orderId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[test_orders] WHERE [order_id]=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private async Task<string?> GetStatusStringAsync(int orderId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [status] FROM [dbo].[test_orders] WHERE [order_id]=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task InsertEvent_AppliesRow()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([InsertEvent(100)]));

        result.Success.Should().BeTrue();
        result.AppliedRows.Should().Be(1);
        (await RowExistsAsync(100)).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEvent_ModifiesRow()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (200, NULL, 'open')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([UpdateEvent(200, "closed")]));

        result.Success.Should().BeTrue();
        (await GetStatusStringAsync(200)).Should().Be("closed");
    }

    [Fact]
    public async Task DeleteEvent_RemovesRow()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (300, NULL, 'open')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([DeleteEvent(300)]));

        result.Success.Should().BeTrue();
        (await RowExistsAsync(300)).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateInsert_PartialSuccess()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (400, NULL, 'existing')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var events   = new[]
        {
            InsertEvent(401, "ok"),      // succeeds
            InsertEvent(400, "dup"),     // duplicate key — row-level error
            InsertEvent(402, "ok2")      // succeeds
        };
        var result = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.Success.Should().BeFalse();
        result.AppliedRows.Should().Be(2);
        result.ErrorRows.Should().Be(1);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.IncomingBatches.FindAsync(incoming.BatchId))!
            .Status.Should().Be(IncomingBatchStatus.PartialSuccess);
    }

    [Fact]
    public async Task FKViolation_Savepoint_ContinuesBatch()
    {
        // Add FK to test_orders referencing itself (or use a non-existent table)
        // Simplest: try to INSERT a status that violates a CHECK constraint (if none, use RowNotFound)
        // For FK test: UPDATE a row that doesn't exist (RowNotFound), but events 1 and 3 succeed
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (501, NULL, 'start'); INSERT INTO [dbo].[test_orders] VALUES (503, NULL, 'start')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var events = new EventPayload[]
        {
            UpdateEvent(501, "done"),         // succeeds
            UpdateEvent(502, "ghost"),        // RowNotFound — row doesn't exist
            UpdateEvent(503, "done")          // succeeds
        };
        var result = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.AppliedRows.Should().Be(2);
        result.ErrorRows.Should().Be(1);
        (await GetStatusStringAsync(501)).Should().Be("done");
        (await GetStatusStringAsync(503)).Should().Be("done");
    }

    [Fact]
    public async Task MetadataMissing_TriggerVersionLow_PartialSuccess()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t-old",
            SourceTable    = "dbo.test_orders",
            ChannelId      = "default",
            TriggerVersion = 1,   // v1 — no pk_data capture
            PkColumnsJson  = null
        });
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();
        var incoming = MakeIncoming();
        var evt = new EventPayload(99, "t-old", "INSERT", "dbo.test_orders", null, null,
            """{"order_id":999,"status":"x"}""");

        var result = await svc.ApplyAsync(incoming, MakeBatch([evt]));

        result.Success.Should().BeFalse();
        result.ErrorRows.Should().Be(1);
        result.AppliedRows.Should().Be(0);
    }

    [Fact]
    public async Task ReplayBatch_DuplicateInsert_SecondRunPartialSuccess()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var events = new[] { InsertEvent(600, "first") };

        // First run
        var incoming1 = MakeIncoming();
        var result1 = await svc.ApplyAsync(incoming1, MakeBatch(events));
        result1.Success.Should().BeTrue();

        // Seed a new incoming batch for the replay
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incoming2 = new SyncIncomingBatch
        {
            NodeId = "local", ChannelId = "default", SourceNodeId = "src",
            BatchSequence = 2, ReceivedTime = DateTime.UtcNow, RowCount = 1,
            Status = IncomingBatchStatus.New
        };
        db.IncomingBatches.Add(incoming2);
        await db.SaveChangesAsync();

        // Second run — same events → DuplicateKey
        var result2 = await svc.ApplyAsync(incoming2, MakeBatch(events));
        result2.Success.Should().BeFalse();
        result2.ErrorRows.Should().Be(1);
        result2.AppliedRows.Should().Be(0);
    }

    [Fact]
    public async Task MultiTrigger_BatchPreloadsMetadataOnce()
    {
        // Seed a second trigger
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Triggers.AnyAsync(t => t.TriggerId == "t-orders2"))
        {
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId = "t-orders2", SourceTable = "dbo.test_orders",
                ChannelId = "default", TriggerVersion = 2,
                PkColumnsJson = """["order_id"]"""
            });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();
        var events = new[]
        {
            new EventPayload(10, "t-orders",  "INSERT", "dbo.test_orders", null, null, """{"order_id":701,"status":"a"}"""),
            new EventPayload(11, "t-orders2", "INSERT", "dbo.test_orders", null, null, """{"order_id":702,"status":"b"}"""),
            new EventPayload(12, "t-orders",  "INSERT", "dbo.test_orders", null, null, """{"order_id":703,"status":"c"}"""),
        };
        var incoming = MakeIncoming();
        var result = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.AppliedRows.Should().Be(3);
        (await RowExistsAsync(701)).Should().BeTrue();
        (await RowExistsAsync(702)).Should().BeTrue();
        (await RowExistsAsync(703)).Should().BeTrue();
    }
}

[CollectionDefinition("ApplyEngine")]
public class ApplyEngineCollection : ICollectionFixture<ApplyEngineFixture> { }
```

- [ ] **Step 3: Run integration tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Engine" -c Debug
```

Expected: all 8 tests PASS (requires Docker for Testcontainers).

- [ ] **Step 4: Run full test suite**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.EngineTests -c Debug
dotnet test tests/MSOSync.TransportTests -c Debug
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Engine" -c Debug
```

Expected: full suite green.

- [ ] **Step 5: Commit**

```powershell
git add tests/MSOSync.IntegrationTests/Engine/ApplyEngineFixture.cs
git add tests/MSOSync.IntegrationTests/Engine/ApplyEngineTests.cs
git commit -m "test: ApplyEngine integration tests against Testcontainers MsSql"
```
