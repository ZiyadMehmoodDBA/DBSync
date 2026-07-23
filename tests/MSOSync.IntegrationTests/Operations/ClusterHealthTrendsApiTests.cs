using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class ClusterHealthTrendsApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/health-trends";

    [Fact]
    public async Task GetHealthTrends_DefaultWindow_Returns200WithCorrectShape()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be("6h");
        body.GetProperty("bucketCount").GetInt32().Should().Be(12);
        body.GetProperty("buckets").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("nodeProbeStats").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Theory]
    [InlineData("1h",  12)]
    [InlineData("6h",  12)]
    [InlineData("24h", 12)]
    [InlineData("7d",  14)]
    public async Task GetHealthTrends_AllWindows_Return200WithCorrectBucketCount(string window, int expectedCount)
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync($"{Base}?window={window}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("window").GetString().Should().Be(window);
        body.GetProperty("bucketCount").GetInt32().Should().Be(expectedCount);
        body.GetProperty("buckets").GetArrayLength().Should().Be(expectedCount);
    }

    [Fact]
    public async Task GetHealthTrends_InvalidWindow_Returns400()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync($"{Base}?window=99h");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHealthTrends_NoToken_Returns401()
    {
        var client = fixture.CreateClient();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
