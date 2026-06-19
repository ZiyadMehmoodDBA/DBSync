using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Metadata;

[Collection("Metadata")]
public sealed class MetadataTests(MetadataFixture factory)
{
    private HttpClient Client() => factory.CreateClient();

    private async Task<string> GetAdminTokenAsync()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = factory.AdminUsername,
            Password = factory.AdminPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        return body!.Token;
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var token = await GetAdminTokenAsync();
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ParameterUpdate_WritesHistoryRow_InDatabase()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.PutAsJsonAsync(
            "/api/v1/parameters/sync.batch.size",
            new { Value = "500" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var histResp = await client.GetAsync("/api/v1/parameters/sync.batch.size/history");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hist = await histResp.Content.ReadFromJsonAsync<List<HistoryItem>>();
        hist.Should().NotBeEmpty();
        hist![0].NewValue.Should().Be("500");
    }

    [Fact]
    public async Task ParameterUpdate_UnknownName_Returns404()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.PutAsJsonAsync(
            "/api/v1/parameters/no.such.param",
            new { Value = "x" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Code.Should().Be("PARAMETER_NOT_FOUND");
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerCrud_CreateGetUpdateDelete_RoundTrip()
    {
        var client = await AuthorizedClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-int-1",
            SourceTable = "dbo.Orders",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = false
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResp = await client.GetAsync("/api/v1/triggers/t-int-1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var trigger = await getResp.Content.ReadFromJsonAsync<TriggerItem>();
        trigger!.TriggerId.Should().Be("t-int-1");
        trigger.TriggerVersion.Should().Be(1);

        var updateResp = await client.PutAsJsonAsync("/api/v1/triggers/t-int-1", new
        {
            SourceTable = "dbo.OrdersV2",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = false,
            SyncOnDelete = false
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<TriggerItem>();
        updated!.TriggerVersion.Should().Be(2);
        updated.SourceTable.Should().Be("dbo.OrdersV2");

        var deleteResp = await client.DeleteAsync("/api/v1/triggers/t-int-1");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getDeleted = await client.GetAsync("/api/v1/triggers/t-int-1");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateTrigger_Returns409Conflict()
    {
        var client = await AuthorizedClientAsync();

        await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-dup-int",
            SourceTable = "dbo.X",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = true
        });

        var resp = await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-dup-int",
            SourceTable = "dbo.X",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = true
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Code.Should().Be("DUPLICATE_TRIGGER");
    }

    // ── Channels ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelCrud_CreateGetUpdateDelete_RoundTrip()
    {
        var client = await AuthorizedClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/channels", new
        {
            ChannelId = "ch-int-1",
            Priority = 5,
            BatchSize = 500,
            MaxBatchToSend = 5,
            MaxDataSize = 2097152
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResp = await client.GetAsync("/api/v1/channels/ch-int-1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var channel = await getResp.Content.ReadFromJsonAsync<ChannelItem>();
        channel!.ChannelId.Should().Be("ch-int-1");
        channel.BatchSize.Should().Be(500);

        var updateResp = await client.PutAsJsonAsync("/api/v1/channels/ch-int-1", new
        {
            Priority = 10,
            BatchSize = 250,
            MaxBatchToSend = 3,
            MaxDataSize = 1048576
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResp = await client.DeleteAsync("/api/v1/channels/ch-int-1");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Exception Handler ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExceptionHandler_NotFound_Returns404WithEnvelope()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.GetAsync("/api/v1/parameters/does-not-exist");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Status.Should().Be(404);
        body.Code.Should().NotBeNullOrEmpty();
        body.Message.Should().NotBeNullOrEmpty();
        body.CorrelationId.Should().NotBeNullOrEmpty();
    }

    // ── Routers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouterSourceGroup_FiltersCorrectly()
    {
        var client = await AuthorizedClientAsync();

        await client.PostAsJsonAsync("/api/v1/routers", new
        {
            RouterId = "r-int-src",
            SourceNodeGroup = "src-group",
            TargetNodeGroup = "tgt-group",
            RouterType = "default"
        });

        await client.PostAsJsonAsync("/api/v1/routers", new
        {
            RouterId = "r-int-other",
            SourceNodeGroup = "other-group",
            TargetNodeGroup = "tgt-group",
            RouterType = "default"
        });

        var resp = await client.GetAsync("/api/v1/routers/source/src-group");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var routers = await resp.Content.ReadFromJsonAsync<List<RouterItem>>();
        routers.Should().ContainSingle(r => r.RouterId == "r-int-src");
        routers.Should().NotContain(r => r.RouterId == "r-int-other");
    }

    // ── Metadata Summary ──────────────────────────────────────────────────────

    [Fact]
    public async Task MetadataSummary_ReturnsCountsObject()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.GetAsync("/api/v1/metadata");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MetadataSummary>();
        body.Should().NotBeNull();
        body!.Parameters.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── Helper record types ───────────────────────────────────────────────────

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorEnvelope(int Status, string Error, string Code, string Message, string CorrelationId);
    private sealed record HistoryItem(string ParameterName, string? OldValue, string? NewValue);
    private sealed record TriggerItem(string TriggerId, string SourceTable, int TriggerVersion);
    private sealed record ChannelItem(string ChannelId, int BatchSize);
    private sealed record RouterItem(string RouterId, string SourceNodeGroup);
    private sealed record MetadataSummary(int Nodes, int Triggers, int Routers, int Channels, int Parameters);
}
