using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class SchedulerStatusEndpointTests(SystemFixture fixture)
{
    [Fact]
    public async Task GET_scheduler_status_returns_200_for_admin()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_scheduler_status_returns_401_for_unauthenticated()
    {
        var client = fixture.CreateClient(); // no auth header

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_scheduler_status_returns_403_for_viewer()
    {
        var client = await fixture.ViewerClientAsync();

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_scheduler_status_response_has_instanceId_and_jobs_array()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/scheduler-status");

        response.GetProperty("instanceId").GetString().Should().MatchRegex(@"^.+:\d+$");
        response.GetProperty("jobs").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_scheduler_status_jobs_have_expected_shape_when_statuses_exist()
    {
        // Pre-seed some status via the ISchedulerHealthReporter registered in the fixture
        await using var scope = fixture.Services.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<ISchedulerHealthReporter>();
        reporter.RecordRunning("SyncJob",  "HOST:1234", DateTimeOffset.UtcNow);
        reporter.RecordStandby("PullJob");
        reporter.RecordIdle("PurgeJob");

        var client   = await fixture.AdminClientAsync();
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/scheduler-status");
        var jobs     = response.GetProperty("jobs").EnumerateArray().ToArray();

        jobs.Should().HaveCountGreaterOrEqualTo(3);
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "SyncJob" &&
            j.GetProperty("mode").GetString()    == "Running");
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "PullJob" &&
            j.GetProperty("mode").GetString()    == "Standby");
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "PurgeJob" &&
            j.GetProperty("mode").GetString()    == "Idle");
    }

    [Fact]
    public async Task GET_health_reflects_standby_state_as_healthy()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<ISchedulerHealthReporter>();
        reporter.RecordStandby("SyncJob");
        reporter.RecordStandby("PullJob");
        reporter.RecordStandby("PurgeJob");
        reporter.RecordStandby("RetryJob");

        var client   = await fixture.AdminClientAsync();
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/system/health");

        var schedulerEntry = response!
            .Where(e => e.GetProperty("name").GetString() == "Scheduler")
            .ToArray();

        schedulerEntry.Should().HaveCountGreaterOrEqualTo(1, "Scheduler contributor should be registered");

        var entry = schedulerEntry.First();
        entry.GetProperty("level").GetString().Should().Be("Healthy");
        entry.GetProperty("summary").GetString().Should().Contain("standby");
    }
}
