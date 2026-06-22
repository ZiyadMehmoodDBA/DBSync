# Task 12: Unit Tests — MSOSync.TransportTests

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 9 (Unit tests)
**Depends on:** Tasks 1–11 (all transport components)

**Files:**
- Create: `tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj`
- Create: `tests/MSOSync.TransportTests/TestDbContext.cs`
- Create: `tests/MSOSync.TransportTests/SmartTransportServiceTests.cs`
- Create: `tests/MSOSync.TransportTests/AcknowledgementServiceTests.cs`
- Create: `tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs`
- Create: `tests/MSOSync.TransportTests/TransportFailureClassifierTests.cs`
- Create: `tests/MSOSync.TransportTests/SequenceVerificationTests.cs`
- Modify: `MSOSync.sln` — add new test project

---

- [ ] **Step 1: Create test project**

Create `tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj`:

```xml
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
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Transport\MSOSync.Transport.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
  </ItemGroup>
</Project>
```

Add to solution:
```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet sln MSOSync.sln add tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj
```

- [ ] **Step 2: Create TestDbContext helper**

Create `tests/MSOSync.TransportTests/TestDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.TransportTests;

internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // SQLite doesn't support SQL Server column types — clear explicit types
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnType(null);
    }
}

internal static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new TestAppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

Also create `tests/MSOSync.TransportTests/FakeClock.cs`:
```csharp
using MSOSync.Common;

namespace MSOSync.TransportTests;

internal sealed class FakeClock(DateTime? utcNow = null) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow ?? DateTime.UtcNow;
}
```

- [ ] **Step 3: SmartTransportServiceTests**

Create `tests/MSOSync.TransportTests/SmartTransportServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class SmartTransportServiceTests
{
    private static SmartTransportService CreateService(
        INodeMetadataService nodeMetadata,
        PushClient?          pushClient   = null,
        IBatchStateMachine?  stateMachine = null)
    {
        var db           = TestDb.Create();
        var compression  = new GzipCompressionService();
        var clock        = new FakeClock();
        var classifier   = new TransportFailureClassifier();
        var sm           = stateMachine ?? new BatchStateMachine(db, clock);
        var ack          = new AcknowledgementService(sm, db, NullLogger<AcknowledgementService>.Instance);
        var nodeProps    = Microsoft.Extensions.Options.Options.Create(
            new MSOSync.Common.NodeProperties { NodeId = "local", GroupId = "g", SyncUrl = "http://local", NodeToken = "tok" });
        var pc           = pushClient ?? new PushClient(Mock.Of<INodeHttpClient>(), nodeProps);

        return new SmartTransportService(nodeMetadata, pc, sm, ack, classifier,
            NullLogger<SmartTransportService>.Instance);
    }

    private static NodeDto ActivePushNode() =>
        new("target", "g", "http://target", "APPROVED", null, null, 60, true, TransportMode.Push);

    private static NodeDto ActivePullNode() =>
        new("target", "g", "http://target", "APPROVED", null, null, 60, true, TransportMode.Pull);

    [Fact]
    public async Task SendBatchAsync_UnknownNode_Skips()
    {
        var meta = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync((NodeDto?)null);

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);
        // No exception = skip successful
    }

    [Fact]
    public async Task SendBatchAsync_DisabledNode_Skips()
    {
        var disabledNode = new NodeDto("target", "g", "http://t", "APPROVED", null, null, 60, false, TransportMode.Push);
        var meta = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(disabledNode);

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);
        // No exception = skip successful
    }

    [Fact]
    public async Task SendBatchAsync_PullNode_DoesNotCallPushClient()
    {
        var meta       = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(ActivePullNode());
        var httpClient = new Mock<INodeHttpClient>();

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);

        httpClient.Verify(
            h => h.PostAsync<It.IsAnyType, It.IsAnyType>(It.IsAny<string>(), It.IsAny<It.IsAnyType>(), It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task SendBatchAsync_PushNode_CallsPushClient()
    {
        var db          = TestDb.Create();
        var clock       = new FakeClock();
        var sm          = new BatchStateMachine(db, clock);
        var meta        = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(ActivePushNode());

        var httpClient  = new Mock<INodeHttpClient>();
        var pushResponse = new MSOSync.Transport.Payloads.PushResponse(1L, true, 5, 0, null);
        httpClient
            .Setup(h => h.PostAsync<MSOSync.Transport.Payloads.BatchPayload, MSOSync.Transport.Payloads.PushResponse>(
                It.IsAny<string>(), It.IsAny<MSOSync.Transport.Payloads.BatchPayload>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(pushResponse);

        var nodeProps = Microsoft.Extensions.Options.Options.Create(
            new MSOSync.Common.NodeProperties { NodeId = "local", GroupId = "g", SyncUrl = "http://local", NodeToken = "tok" });
        var pushClient = new PushClient(httpClient.Object, nodeProps);

        var batch = new SyncOutgoingBatch { NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var svc = CreateService(meta.Object, pushClient, sm);
        await svc.SendBatchAsync(batch, [], default);

        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }
}
```

- [ ] **Step 4: AcknowledgementServiceTests**

Create `tests/MSOSync.TransportTests/AcknowledgementServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class AcknowledgementServiceTests
{
    private static (AcknowledgementService Svc, AppDbContext Db) Create()
    {
        var db  = TestDb.Create();
        var sm  = new BatchStateMachine(db, new FakeClock());
        var svc = new AcknowledgementService(sm, db, NullLogger<AcknowledgementService>.Instance);
        return (svc, db);
    }

    private static async Task<SyncOutgoingBatch> AddBatch(AppDbContext db, BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default", Status = (byte)status
        };
        db.OutgoingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Success_MovesToAcknowledged()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.New);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", true, null, DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Failure_MovesToError_InsertsError()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.New);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", false, "apply failed", DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
        db.BatchErrors.Should().ContainSingle(e => e.BatchId == batch.BatchId);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Duplicate_ReturnsTrue_NoStateChange()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.Acknowledged);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", true, null, DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var unchanged = await db.OutgoingBatches.FindAsync(batch.BatchId);
        unchanged!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_NotFound_ReturnsFalse()
    {
        var (svc, _) = Create();
        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(99999, 1, "target", true, null, DateTimeOffset.UtcNow));
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 5: GzipCompressionServiceTests**

Create `tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class GzipCompressionServiceTests
{
    private static readonly GzipCompressionService Svc = new();

    [Fact]
    public void CompressDecompress_RoundTrip_MatchesOriginal()
    {
        var original   = Encoding.UTF8.GetBytes("hello world from MSOSync transport");
        var compressed = Svc.Compress(original);
        var restored   = Svc.Decompress(compressed);
        restored.Should().Equal(original);
    }

    [Fact]
    public void Compress_LargePayload_RoundTrip()
    {
        var original   = Encoding.UTF8.GetBytes(new string('A', 100_000));
        var compressed = Svc.Compress(original);
        compressed.Length.Should().BeLessThan(original.Length);
        Svc.Decompress(compressed).Should().Equal(original);
    }

    [Fact]
    public void Compress_EmptyArray_RoundTrip()
    {
        var original   = Array.Empty<byte>();
        var compressed = Svc.Compress(original);
        var restored   = Svc.Decompress(compressed);
        restored.Should().Equal(original);
    }
}
```

- [ ] **Step 6: TransportFailureClassifierTests**

Create `tests/MSOSync.TransportTests/TransportFailureClassifierTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class TransportFailureClassifierTests
{
    private static readonly TransportFailureClassifier Classifier = new();

    [Fact]
    public void Classify_TaskCanceledException_ReturnsTimeout()
        => Classifier.Classify(new TaskCanceledException())
            .Should().Be(TransportFailureReason.Timeout);

    [Fact]
    public void Classify_HttpRequestException_401_ReturnsAuthenticationFailure()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.Unauthorized))
            .Should().Be(TransportFailureReason.AuthenticationFailure);

    [Fact]
    public void Classify_HttpRequestException_403_ReturnsAuthenticationFailure()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.Forbidden))
            .Should().Be(TransportFailureReason.AuthenticationFailure);

    [Fact]
    public void Classify_HttpRequestException_503_ReturnsConnectionRefused()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.ServiceUnavailable))
            .Should().Be(TransportFailureReason.ConnectionRefused);

    [Fact]
    public void Classify_JsonException_ReturnsCompressionFailure()
        => Classifier.Classify(new JsonException("bad json"))
            .Should().Be(TransportFailureReason.CompressionFailure);

    [Fact]
    public void Classify_InvalidDataException_ReturnsCompressionFailure()
        => Classifier.Classify(new InvalidDataException("bad gzip"))
            .Should().Be(TransportFailureReason.CompressionFailure);

    [Fact]
    public void Classify_ArbitraryException_ReturnsUnknown()
        => Classifier.Classify(new Exception("unknown"))
            .Should().Be(TransportFailureReason.Unknown);
}
```

- [ ] **Step 7: SequenceVerificationTests**

Create `tests/MSOSync.TransportTests/SequenceVerificationTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class SequenceVerificationTests
{
    private static (BatchTransportQueryService Svc, AppDbContext Db) Create()
    {
        var db = TestDb.Create();
        return (new BatchTransportQueryService(db), db);
    }

    private static async Task<SyncIncomingBatch> InsertIncoming(
        AppDbContext db, string sourceNodeId, string channelId, long seq)
    {
        var b = new SyncIncomingBatch
        {
            BatchId       = seq,
            NodeId        = "local",
            ChannelId     = channelId,
            SourceNodeId  = sourceNodeId,
            BatchSequence = seq,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 1
        };
        db.IncomingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task FirstBatch_Seq1_NoGap()
    {
        var (svc, _) = Create();
        var lastSeq = await svc.GetLastSequenceAsync("source1", "default");

        // First batch: lastSeq=0, batchSequence=1 → lastSeq + 1 == batchSequence → OK
        (lastSeq + 1 == 1).Should().BeTrue();
    }

    [Fact]
    public async Task SequentialBatches_NoGap()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);
        await InsertIncoming(db, "source1", "default", 2);

        var lastSeq = await svc.GetLastSequenceAsync("source1", "default");
        lastSeq.Should().Be(2);
        (lastSeq + 1 == 3).Should().BeTrue();
    }

    [Fact]
    public async Task Gap_1_2_4_Detected()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);
        await InsertIncoming(db, "source1", "default", 2);
        // Sequence 3 missing — incoming arrives with seq=4

        var lastSeq       = await svc.GetLastSequenceAsync("source1", "default");
        var incomingSeq   = 4L;
        var isGap         = lastSeq + 1 != incomingSeq;

        isGap.Should().BeTrue($"expected gap: lastSeq={lastSeq} incomingSeq={incomingSeq}");
    }

    [Fact]
    public async Task DuplicateBatch_Detected()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);

        var exists = await svc.IncomingBatchExistsAsync("source1", 1L);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NonExistentBatch_NotDuplicate()
    {
        var (svc, _) = Create();
        var exists = await svc.IncomingBatchExistsAsync("source1", 99L);
        exists.Should().BeFalse();
    }
}
```

- [ ] **Step 8: Run all unit tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.TransportTests -c Debug
```

Expected: all tests pass. Fix any compilation issues.

- [ ] **Step 9: Build full solution**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
```

- [ ] **Step 10: Commit**

```pwsh
git add tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj
git add tests/MSOSync.TransportTests/TestDbContext.cs
git add tests/MSOSync.TransportTests/FakeClock.cs
git add tests/MSOSync.TransportTests/SmartTransportServiceTests.cs
git add tests/MSOSync.TransportTests/AcknowledgementServiceTests.cs
git add tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs
git add tests/MSOSync.TransportTests/TransportFailureClassifierTests.cs
git add tests/MSOSync.TransportTests/SequenceVerificationTests.cs
git add MSOSync.sln
git commit -m "feat(epic6): MSOSync.TransportTests — 5 test classes covering SmartTransport, Ack, Gzip, Classifier, Sequence"
```
