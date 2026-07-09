// tests/MSOSync.IntegrationTests/System/WorkerStatusTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Workers;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

/// <summary>
/// Worker status tests operate against the DI container and also the HTTP endpoint.
/// Workers register themselves via IWorkerStatusRegistry (in-process).
/// </summary>
[Collection("SystemAdmin")]
public sealed class WorkerStatusTests(SystemFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── In-process registry tests ──────────────────────────────────────────────

    [Fact]
    public async Task Registry_GetAll_ReturnsWorkerStatusDtos()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        // Register a test worker and verify it appears
        registry.Register("test-worker-status", TimeSpan.FromSeconds(30));
        var workers = registry.GetAll();

        workers.Should().NotBeNull("GetAll must return an array");
        workers.Should().Contain(w => w.WorkerName == "test-worker-status",
            "a freshly registered worker must appear in GetAll");
    }

    [Fact]
    public async Task Registry_RegisteredWorker_HasNonEmptyName()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-name-check", TimeSpan.FromSeconds(60));
        var workers = registry.GetAll();

        workers.Should().AllSatisfy(w =>
            w.WorkerName.Should().NotBeNullOrEmpty("each worker must have a non-empty name"));
    }

    [Fact]
    public async Task Registry_RegisteredWorker_HasValidState()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-state-check", TimeSpan.FromSeconds(30));
        var workers = registry.GetAll();

        var validStates = Enum.GetValues<WorkerState>().Select(s => s.ToString()).ToArray();
        workers.Should().AllSatisfy(w =>
            validStates.Should().Contain(w.State.ToString(),
                $"worker '{w.WorkerName}' has an unrecognized state '{w.State}'"));
    }

    [Fact]
    public async Task Registry_GetOne_ReturnsRegisteredWorker()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-getone", TimeSpan.FromSeconds(30));
        var dto = registry.GetOne("test-getone");

        dto.Should().NotBeNull();
        dto.WorkerName.Should().Be("test-getone");
    }

    [Fact]
    public async Task Registry_RecordTickStart_SetsRunningState()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-tick-start", TimeSpan.FromSeconds(30));
        registry.RecordTickStart("test-tick-start");
        var dto = registry.GetOne("test-tick-start");

        dto.ExecutionState.Should().Be(WorkerExecutionState.Running,
            "after RecordTickStart the execution state must be Running");
    }

    [Fact]
    public async Task Registry_RecordTickComplete_SetsIdleState()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-tick-complete", TimeSpan.FromSeconds(30));
        registry.RecordTickStart("test-tick-complete");
        registry.RecordTickComplete("test-tick-complete");
        var dto = registry.GetOne("test-tick-complete");

        dto.ExecutionState.Should().Be(WorkerExecutionState.Idle,
            "after RecordTickComplete the execution state must be Idle");
    }

    [Fact]
    public async Task Registry_ThreeConsecutiveFailures_SetsWarningState()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-warning-state", TimeSpan.FromSeconds(30));
        var ex = new InvalidOperationException("simulated failure");
        for (var i = 0; i < 3; i++)
        {
            registry.RecordTickStart("test-warning-state");
            registry.RecordTickFailed("test-warning-state", ex);
        }

        var dto = registry.GetOne("test-warning-state");

        dto.State.Should().Be(WorkerState.Warning,
            "3 consecutive failures must transition the worker to Warning state");
        dto.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public async Task Registry_FiveConsecutiveFailures_SetsFailedState()
    {
        await using var scope    = fx.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkerStatusRegistry>();

        registry.Register("test-failed-state", TimeSpan.FromSeconds(30));
        var ex = new InvalidOperationException("simulated failure");
        for (var i = 0; i < 5; i++)
        {
            registry.RecordTickStart("test-failed-state");
            registry.RecordTickFailed("test-failed-state", ex);
        }

        var dto = registry.GetOne("test-failed-state");

        dto.State.Should().Be(WorkerState.Failed,
            "5 consecutive failures must transition the worker to Failed state");
        dto.ConsecutiveFailures.Should().Be(5);
    }

    // ── HTTP endpoint tests ────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkers_Admin_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/workers");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var workers = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        workers.Should().NotBeNull("endpoint must return an array of worker status DTOs");
    }

    [Fact]
    public async Task GetWorkers_Viewer_Returns200()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.GetAsync("/api/v1/system/workers");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "VIEWER role should have read access to worker status");
    }

    [Fact]
    public async Task GetWorkers_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/workers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkers_ResponseItems_HaveWorkerName()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/workers");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        if (items is { Length: > 0 })
        {
            items.Should().AllSatisfy(w =>
                w.TryGetProperty("workerName", out var name)
                    .Should().BeTrue("each worker item must include workerName"));
        }
    }
}
