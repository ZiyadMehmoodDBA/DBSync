using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Options;
using MSOSync.Scheduler.Workers;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class ReplayWorkerRegistryTests
{
    [Fact]
    public async Task ReplayWorker_Registers_With_IWorkerStatusRegistry_On_Start()
    {
        var registry = new Mock<IWorkerStatusRegistry>();
        var services = new ServiceCollection().BuildServiceProvider();

        var worker = new ReplayWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReplayOptions { WorkerIntervalSeconds = 10 }),
            registry.Object,
            NullLogger<ReplayWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(cts.Token);

        registry.Verify(r => r.Register(nameof(ReplayWorker), It.IsAny<TimeSpan>()), Times.Once);
    }
}
