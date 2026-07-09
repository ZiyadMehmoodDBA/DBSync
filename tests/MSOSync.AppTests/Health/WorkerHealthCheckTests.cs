using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.App.Health;
using MSOSync.App.Workers;
using MSOSync.Common.Workers;
using Xunit;

namespace MSOSync.AppTests.Health;

public sealed class WorkerHealthCheckTests
{
    private static WorkerStatusRegistry CreateRegistryWithWorkers(
        Action<WorkerStatusRegistry> configure)
    {
        var publisherMock = new Mock<IPublisher>();
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var registry = new WorkerStatusRegistry(publisherMock.Object, NullLogger<WorkerStatusRegistry>.Instance);
        configure(registry);
        return registry;
    }

    // Test 1: All workers healthy => HealthCheckResult.Healthy
    [Fact]
    public async Task CheckHealthAsync_AllWorkersHealthy_ReturnsHealthy()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("Worker1", TimeSpan.FromSeconds(30));
            r.Register("Worker2", TimeSpan.FromSeconds(60));
            // Complete a tick so each worker is Idle (Healthy)
            r.RecordTickStart("Worker1"); r.RecordTickComplete("Worker1");
            r.RecordTickStart("Worker2"); r.RecordTickComplete("Worker2");
        });

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // Test 2: One worker Warning => HealthCheckResult.Degraded
    [Fact]
    public async Task CheckHealthAsync_OneWorkerWarning_ReturnsDegraded()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("GoodWorker", TimeSpan.FromSeconds(30));
            r.Register("BadWorker", TimeSpan.FromSeconds(30));
            r.RecordTickStart("GoodWorker"); r.RecordTickComplete("GoodWorker");
            // 3 failures = Warning
            for (int i = 0; i < 3; i++)
            {
                r.RecordTickStart("BadWorker");
                r.RecordTickFailed("BadWorker", new Exception("error"));
            }
        });

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // Test 3: One worker Failed => HealthCheckResult.Unhealthy
    [Fact]
    public async Task CheckHealthAsync_OneWorkerFailed_ReturnsUnhealthy()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("CriticalWorker", TimeSpan.FromSeconds(30));
            // 5 failures = Failed
            for (int i = 0; i < 5; i++)
            {
                r.RecordTickStart("CriticalWorker");
                r.RecordTickFailed("CriticalWorker", new Exception("fatal"));
            }
        });

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // Test 4: Empty registry => HealthCheckResult.Healthy
    [Fact]
    public async Task CheckHealthAsync_EmptyRegistry_ReturnsHealthy()
    {
        var registry = CreateRegistryWithWorkers(_ => { });

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No workers", result.Description);
    }
}
