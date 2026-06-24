// tests/MSOSync.IntegrationTests/OperationalRead/BatchErrorsTests.cs
using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class BatchErrorsTests(OperationalReadFixture fixture)
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
    public async Task GetBatchErrors_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors");

        result!.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetBatchErrors_FilterBySeverity_ReturnsWarning()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors?severity=Warning");

        result!.Items.Should().OnlyContain(e => e.Severity == "Warning");
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetBatchErrorById_Existing_Returns200()
    {
        var client = await AuthenticatedClientAsync();

        var list = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors");
        var id = list!.Items.First().ErrorId;

        var resp = await client.GetAsync($"api/v1/batch-errors/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBatchErrorById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/batch-errors/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBatchErrorSummary_ReturnsCorrectCounts()
    {
        var client = await AuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            "api/v1/batch-errors/summary");

        dto!.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(1);
        dto.Total.Should().Be(dto.Info + dto.Warning + dto.Critical);
    }

    [Fact]
    public async Task GetBatchErrorSummary_FilterByBatchId_ScopesCounts()
    {
        var client = await AuthenticatedClientAsync();

        // Discover the batchId that has 3 errors (Timeout + Deadlock + MetadataMissing)
        // by finding the batch error with ConflictType "Deadlock" (unique to the 3-error batch)
        var list   = await client.GetFromJsonAsync<PagedResult<BatchErrorSummaryDto>>(
            "api/v1/batch-errors");
        var target = list!.Items.First(e => e.ConflictType == "Deadlock");

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            $"api/v1/batch-errors/summary?batchId={target.BatchId}");

        dto!.Total.Should().Be(3);  // Timeout + Deadlock + MetadataMissing
    }

    [Fact]
    public async Task GetBatchErrorSummary_FilterByFrom_CountsTodayOnly()
    {
        var client = await AuthenticatedClientAsync();
        var from   = DateTime.UtcNow.Date.ToString("O");

        var dto = await client.GetFromJsonAsync<BatchErrorSummaryCountDto>(
            $"api/v1/batch-errors/summary?from={Uri.EscapeDataString(from)}");

        dto!.Total.Should().Be(2);  // Deadlock + MetadataMissing seeded as today
    }

    [Fact]
    public async Task GetBatchErrors_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/batch-errors");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
