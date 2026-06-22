# Task 3: Transport Module Scaffolding — Wire DTOs + Compression + Failure Types

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 3, § 5
**Depends on:** Task 1 (enums for PingResponse)

**Files:**
- Create: `src/MSOSync.Transport/Payloads/EventPayload.cs`
- Create: `src/MSOSync.Transport/Payloads/BatchPayload.cs`
- Create: `src/MSOSync.Transport/Payloads/PullRequest.cs`
- Create: `src/MSOSync.Transport/Payloads/PullResponse.cs`
- Create: `src/MSOSync.Transport/Payloads/AckPayload.cs`
- Create: `src/MSOSync.Transport/Payloads/PushResponse.cs`
- Create: `src/MSOSync.Transport/Payloads/PingResponse.cs`
- Create: `src/MSOSync.Transport/GzipCompressionService.cs`
- Create: `src/MSOSync.Transport/TransportFailureReason.cs`
- Create: `src/MSOSync.Transport/ITransportFailureClassifier.cs`
- Create: `src/MSOSync.Transport/TransportFailureClassifier.cs`
- Create: `src/MSOSync.Transport/TransportJsonContext.cs`
- Delete: `src/MSOSync.Batch/GzipBatchCompressor.cs`
- Modify: `src/MSOSync.Batch/BatchPipelineExtensions.cs` (remove GzipBatchCompressor registration)
- Modify: `src/MSOSync.Transport/MSOSync.Transport.csproj` (add Engine + Metadata references)
- Delete: `src/MSOSync.Transport/Placeholder.cs`

**Interfaces:**
- Produces: All wire payload records; `GzipCompressionService`; `TransportFailureReason`; `ITransportFailureClassifier`/`TransportFailureClassifier`
- Consumed by: Tasks 4, 6, 7, 8, 9, 10, 12

---

- [ ] **Step 1: Update Transport.csproj references**

Current `src/MSOSync.Transport/MSOSync.Transport.csproj` references Common, Persistence, Batch. Add Engine and Metadata:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>PullClient, PushClient, SmartTransportService, AcknowledgementService, GzipCompressionService</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\MSOSync.Metadata\MSOSync.Metadata.csproj" />
  </ItemGroup>
</Project>
```

Note: Transport references Engine for `ITransportService` (defined there). Transport references Metadata for `INodeMetadataService`.

- [ ] **Step 2: Create wire payload DTOs**

Create `src/MSOSync.Transport/Payloads/EventPayload.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

// EventType: char 'I'/'U'/'D' from SyncDataEvent mapped to "INSERT"/"UPDATE"/"DELETE"
public sealed record EventPayload(
    long    EventId,
    string  TriggerId,
    string  EventType,
    string  TableName,
    long?   TransactionId,
    string? PkData,
    string? RowData);
```

Create `src/MSOSync.Transport/Payloads/BatchPayload.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

// Entire HTTP body is gzip-compressed; Events is the uncompressed list
public sealed record BatchPayload(
    long                        BatchId,
    long                        BatchSequence,
    string                      ChannelId,
    string                      SourceNodeId,
    string                      TargetNodeId,
    int                         RowCount,
    IReadOnlyList<EventPayload> Events);
```

Create `src/MSOSync.Transport/Payloads/PullRequest.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

public sealed record PullRequest(
    string TargetNodeId,
    string ChannelId,
    long   AfterSequence);
```

Create `src/MSOSync.Transport/Payloads/PullResponse.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

public sealed record PullResponse(
    IReadOnlyList<BatchPayload> Batches,
    bool                        MoreAvailable);
```

Create `src/MSOSync.Transport/Payloads/AckPayload.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

public sealed record AckPayload(
    long            BatchId,
    long            BatchSequence,
    string          NodeId,
    bool            Success,
    string?         ErrorMessage,
    DateTimeOffset  AckTime);
```

Create `src/MSOSync.Transport/Payloads/PushResponse.cs`:
```csharp
namespace MSOSync.Transport.Payloads;

public sealed record PushResponse(
    long    BatchId,
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
```

Create `src/MSOSync.Transport/Payloads/PingResponse.cs`:
```csharp
using MSOSync.Persistence;

namespace MSOSync.Transport.Payloads;

public sealed record PingResponse(
    string        NodeId,
    string        Status,
    TransportMode TransportMode);
```

- [ ] **Step 3: Create GzipCompressionService**

Create `src/MSOSync.Transport/GzipCompressionService.cs`:
```csharp
using System.IO.Compression;

namespace MSOSync.Transport;

public sealed class GzipCompressionService
{
    public byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var gzip   = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
```

- [ ] **Step 4: Create TransportFailureReason + ITransportFailureClassifier + TransportFailureClassifier**

Create `src/MSOSync.Transport/TransportFailureReason.cs`:
```csharp
namespace MSOSync.Transport;

public enum TransportFailureReason
{
    Timeout,
    HttpError,
    ConnectionRefused,
    CompressionFailure,
    SequenceGap,
    ApplyFailure,
    AuthenticationFailure,
    Unknown
}
```

Create `src/MSOSync.Transport/ITransportFailureClassifier.cs`:
```csharp
namespace MSOSync.Transport;

public interface ITransportFailureClassifier
{
    TransportFailureReason Classify(Exception ex);
}
```

Create `src/MSOSync.Transport/TransportFailureClassifier.cs`:
```csharp
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MSOSync.Transport;

public sealed class TransportFailureClassifier : ITransportFailureClassifier
{
    public TransportFailureReason Classify(Exception ex) => ex switch
    {
        TaskCanceledException                                                  => TransportFailureReason.Timeout,
        OperationCanceledException                                             => TransportFailureReason.Timeout,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }       => TransportFailureReason.AuthenticationFailure,
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden }          => TransportFailureReason.AuthenticationFailure,
        HttpRequestException                                                    => TransportFailureReason.ConnectionRefused,
        InvalidDataException                                                    => TransportFailureReason.CompressionFailure,
        JsonException                                                           => TransportFailureReason.CompressionFailure,
        _                                                                       => TransportFailureReason.Unknown
    };
}
```

- [ ] **Step 5: Create JSON source generation context**

Create `src/MSOSync.Transport/TransportJsonContext.cs`:
```csharp
using System.Text.Json.Serialization;
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

- [ ] **Step 6: Delete GzipBatchCompressor from Batch**

Delete `src/MSOSync.Batch/GzipBatchCompressor.cs`.

Remove the registration from `src/MSOSync.Batch/BatchPipelineExtensions.cs`:
```csharp
// Remove this line:
services.AddSingleton<GzipBatchCompressor>();
```

The updated AddBatchPipeline body:
```csharp
        services.AddScoped<IBatchStateMachine, BatchStateMachine>();
        services.AddScoped<IBatchCreator, BatchCreator>();
        services.AddScoped<RetryProcessor>();
        services.AddScoped<BatchPurger>();
```

Delete `src/MSOSync.Transport/Placeholder.cs` as well.

- [ ] **Step 7: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings, zero errors. If any code still references `GzipBatchCompressor`, fix those call sites.

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Transport/MSOSync.Transport.csproj
git add src/MSOSync.Transport/Payloads/EventPayload.cs
git add src/MSOSync.Transport/Payloads/BatchPayload.cs
git add src/MSOSync.Transport/Payloads/PullRequest.cs
git add src/MSOSync.Transport/Payloads/PullResponse.cs
git add src/MSOSync.Transport/Payloads/AckPayload.cs
git add src/MSOSync.Transport/Payloads/PushResponse.cs
git add src/MSOSync.Transport/Payloads/PingResponse.cs
git add src/MSOSync.Transport/GzipCompressionService.cs
git add src/MSOSync.Transport/TransportFailureReason.cs
git add src/MSOSync.Transport/ITransportFailureClassifier.cs
git add src/MSOSync.Transport/TransportFailureClassifier.cs
git add src/MSOSync.Transport/TransportJsonContext.cs
git rm src/MSOSync.Transport/Placeholder.cs
git rm src/MSOSync.Batch/GzipBatchCompressor.cs
git add src/MSOSync.Batch/BatchPipelineExtensions.cs
git commit -m "feat(epic6): Transport wire DTOs, GzipCompressionService, failure classifier; remove GzipBatchCompressor"
```
