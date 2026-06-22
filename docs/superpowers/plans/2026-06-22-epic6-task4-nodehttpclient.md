# Task 4: INodeHttpClient + NodeHttpClient + Polly

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 5 (INodeHttpClient)
**Depends on:** Task 3 (GzipCompressionService, TransportJsonContext)

**Files:**
- Create: `src/MSOSync.Transport/INodeHttpClient.cs`
- Create: `src/MSOSync.Transport/NodeHttpClient.cs`
- Modify: `src/MSOSync.Transport/MSOSync.Transport.csproj` (add Polly package)

**Interfaces:**
- Produces: `INodeHttpClient` — typed HttpClient wrapper with gzip + auth headers + Polly resilience
- Consumed by: Tasks 6 (SmartTransportService), 7 (PushClient, PullClient)

---

- [ ] **Step 1: Add Polly to Transport.csproj**

The `Microsoft.Extensions.Http.Resilience` package (ships with .NET 9 / Polly 8) is used for resilience pipelines. Update `src/MSOSync.Transport/MSOSync.Transport.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>PullClient, PushClient, SmartTransportService, AcknowledgementService, GzipCompressionService</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
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

- [ ] **Step 2: Write failing test for INodeHttpClient**

We test via `NodeHttpClient` directly using a test `HttpMessageHandler`. Add to `tests/MSOSync.EngineTests/` (or we will add to `MSOSync.TransportTests` in Task 12 — skip this step, tests will be in Task 12).

- [ ] **Step 3: Create INodeHttpClient**

Create `src/MSOSync.Transport/INodeHttpClient.cs`:
```csharp
namespace MSOSync.Transport;

public interface INodeHttpClient
{
    /// <summary>
    /// POST JSON body (gzip-compressed) and deserialize response.
    /// Throws HttpRequestException on non-success status (after Polly retries).
    /// </summary>
    Task<TResponse> PostAsync<TRequest, TResponse>(
        string            url,
        TRequest          body,
        string            nodeId,
        string            nodeToken,
        CancellationToken ct = default);

    /// <summary>
    /// POST and return null on 204 No Content.
    /// </summary>
    Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string            url,
        TRequest          body,
        string            nodeId,
        string            nodeToken,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create NodeHttpClient**

Create `src/MSOSync.Transport/NodeHttpClient.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Transport;

public sealed class NodeHttpClient(
    HttpClient             httpClient,
    GzipCompressionService compression,
    IHttpContextAccessor?  httpContextAccessor = null) : INodeHttpClient
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts)!;
    }

    public async Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var json       = JsonSerializer.Serialize(body, JsonOpts);
        var jsonBytes  = Encoding.UTF8.GetBytes(json);
        var compressed = compression.Compress(jsonBytes);

        var content = new ByteArrayContent(compressed);
        content.Headers.ContentType     = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-Node-Id",    nodeId);
        request.Headers.Add("X-Node-Token", nodeToken);
        request.Headers.Add("Accept-Encoding", "gzip");

        var correlationId = GetOrCreateCorrelationId();
        request.Headers.Add("X-Correlation-Id", correlationId);

        return await httpClient.SendAsync(request, ct);
    }

    private string GetOrCreateCorrelationId()
    {
        var ctx = httpContextAccessor?.HttpContext;
        if (ctx != null && ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var id))
            return id.ToString();
        return Guid.NewGuid().ToString("N");
    }
}
```

- [ ] **Step 5: Verify build**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings, zero errors. The Polly registration happens in Task 11 (`TransportServiceExtensions`).

- [ ] **Step 6: Commit**

```pwsh
git add src/MSOSync.Transport/MSOSync.Transport.csproj
git add src/MSOSync.Transport/INodeHttpClient.cs
git add src/MSOSync.Transport/NodeHttpClient.cs
git commit -m "feat(epic6): INodeHttpClient with gzip + auth headers, Polly resilience wired in Task 11"
```
