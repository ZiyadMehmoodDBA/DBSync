using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.App.Workers;
using MSOSync.Common.Workers;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class WorkerRegistryIntegrationTests
{
    [Fact]
    public void GetAll_After_RegisteringTwoWorkers_ReturnsBoth()
    {
        var publisherMock = new Mock<IPublisher>();
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var registry = new WorkerStatusRegistry(publisherMock.Object, NullLogger<WorkerStatusRegistry>.Instance);

        registry.Register("WorkerAlpha", TimeSpan.FromSeconds(10));
        registry.Register("WorkerBeta", TimeSpan.FromMinutes(5));

        var all = registry.GetAll();

        Assert.Equal(2, all.Length);
        Assert.Contains(all, w => w.WorkerName == "WorkerAlpha" &&
                                   w.ExpectedInterval == TimeSpan.FromSeconds(10));
        Assert.Contains(all, w => w.WorkerName == "WorkerBeta" &&
                                   w.ExpectedInterval == TimeSpan.FromMinutes(5));
    }
}
