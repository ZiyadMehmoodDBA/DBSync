// tests/MSOSync.IntegrationTests/OperationalRead/IncomingBatchesTests.cs
using FluentAssertions;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class IncomingBatchesTests(OperationalReadFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = fixture.CreateClient();
        var token  = await fixture.GetViewerTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetIncomingBatches_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<IncomingBatchSummaryDto>>(
            "api/v1/incoming-batches");

        result!.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetIncomingBatches_FilterByStatus_ReturnsOne()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<IncomingBatchSummaryDto>>(
            "api/v1/incoming-batches?status=Error");

        result!.TotalCount.Should().Be(1);
        result.Items.Single().Status.Should().Be(IncomingBatchStatus.Error);
    }

    [Fact]
    public async Task GetIncomingBatchById_Existing_Returns200()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/incoming-batches/1001");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<IncomingBatchDetailDto>();
        dto!.BatchId.Should().Be(1001L);
        dto.Status.Should().Be(IncomingBatchStatus.Applied);
    }

    [Fact]
    public async Task GetIncomingBatchById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/incoming-batches/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIncomingBatches_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/incoming-batches");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
