using FluentAssertions;
using Moq;
using MSOSync.App.Notifications;
using MSOSync.App.SignalR;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Notifications;
using Xunit;

namespace MSOSync.MetadataTests.Notifications;

public sealed class WorkerFailedNotificationHandlerTests
{
    private readonly Mock<INotificationService> _svc = new();

    [Fact]
    public async Task Handle_WorkerFailed_CallsCreateWithCriticalAllUsers()
    {
        var handler = new WorkerFailedNotificationHandler(_svc.Object);
        var evt     = new WorkerStatusChangedEvent(
            "HeartbeatWorker", WorkerHealthState.Healthy, WorkerHealthState.Failed, DateTime.UtcNow);

        await handler.Handle(evt, default);

        _svc.Verify(s => s.CreateAsync(
            NotificationEventType.WorkerFailed,
            NotificationSeverity.Critical,
            It.Is<string>(t => t.Contains("HeartbeatWorker")),
            It.IsAny<string>(),
            "Worker",
            "HeartbeatWorker",
            It.IsAny<string?>(),
            NotificationAudience.AllUsers,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WorkerWarning_NotHandledByFailedHandler()
    {
        var handler = new WorkerFailedNotificationHandler(_svc.Object);
        var evt     = new WorkerStatusChangedEvent(
            "HeartbeatWorker", WorkerHealthState.Healthy, WorkerHealthState.Warning, DateTime.UtcNow);

        await handler.Handle(evt, default);

        _svc.Verify(s => s.CreateAsync(
            It.IsAny<NotificationEventType>(),
            It.IsAny<NotificationSeverity>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<NotificationAudience>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
