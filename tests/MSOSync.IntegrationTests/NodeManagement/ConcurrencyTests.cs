using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class ConcurrencyTests(NodeManagementFixture fixture)
{
    [Fact]
    public async Task ConcurrentApprove_SameRegistration_OneSucceedsOneFails()
    {
        // Register a fresh node to avoid interference with other tests
        var anon = fixture.AnonymousClient();
        var regResp = await anon.PostAsJsonAsync(
            "api/v1/node-management/registrations",
            new
            {
                externalId = "concurrency-test-node",
                nodeName   = "concurrency-node",
                nodeType   = "source",
            });
        regResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = regBody.GetProperty("registrationId").GetInt64();

        // Two approvers race to approve the same registration
        var client1 = await fixture.ApproverClientAsync();
        var client2 = await fixture.ApproverClientAsync();

        var task1 = client1.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve", new { notes = "approver1" });
        var task2 = client2.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve", new { notes = "approver2" });

        var results = await Task.WhenAll(task1, task2);

        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(x => x).ToList();

        // Exactly one 204, exactly one 409
        statuses.Should().BeEquivalentTo(new[] { 204, 409 });
    }
}
