// tests/MSOSync.IntegrationTests/OperationalRead/EventsTests.cs
using FluentAssertions;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Events;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.OperationalRead;

[Collection("OperationalRead")]
public sealed class EventsTests(OperationalReadFixture fixture)
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
    public async Task GetEvents_ReturnsAllSeeded()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            "api/v1/events");

        result!.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetEvents_FilterByIsProcessed_ReturnsThree()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>(
            "api/v1/events?isProcessed=true");

        result!.TotalCount.Should().Be(3);
        result.Items.Should().OnlyContain(e => e.IsProcessed);
    }

    [Fact]
    public async Task GetEvents_InvalidPage_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events?page=0");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEvents_PageSizeTooLarge_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events?pageSize=101");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEventById_Existing_Returns200WithDto()
    {
        var client = await AuthenticatedClientAsync();

        var list = await client.GetFromJsonAsync<PagedResult<EventSummaryDto>>("api/v1/events");
        var id   = list!.Items.First().EventId;

        var resp = await client.GetAsync($"api/v1/events/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<EventDetailDto>();
        dto!.EventId.Should().Be(id);
    }

    [Fact]
    public async Task GetEventById_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events/9999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvents_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/events");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetEvents_ViewerToken_Returns200()
    {
        var client = await AuthenticatedClientAsync();
        var resp   = await client.GetAsync("api/v1/events");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
