using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.App.SignalR;
using MSOSync.App.Workers;
using MSOSync.Common.Workers;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class WorkerRegistryTests
{
    private static WorkerStatusRegistry CreateRegistry()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        return new WorkerStatusRegistry(scopeFactory.Object, NullLogger<WorkerStatusRegistry>.Instance);
    }

    // Test 1: Register + RecordTickStart => ExecutionState = Running
    [Fact]
    public void RecordTickStart_AfterRegister_StateIsRunning()
    {
        var registry = CreateRegistry();
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        registry.RecordTickStart("TestWorker");

        var dto = registry.GetOne("TestWorker");
        dto.ExecutionState.Should().Be(WorkerExecutionState.Running);
        dto.State.Should().Be(WorkerState.Running);
    }

    // Test 2: RecordTickComplete => ExecutionState = Idle, LastCompleted set
    [Fact]
    public void RecordTickComplete_SetsIdleAndLastCompleted()
    {
        var registry = CreateRegistry();
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        registry.RecordTickStart("TestWorker");
        registry.RecordTickComplete("TestWorker");

        var dto = registry.GetOne("TestWorker");
        dto.ExecutionState.Should().Be(WorkerExecutionState.Idle);
        dto.LastCompleted.Should().NotBeNull();
        dto.LastSuccessfulRun.Should().NotBeNull();
    }

    // Test 3: RecordTickFailed 3 times => HealthState = Warning
    [Fact]
    public void RecordTickFailed_ThreeTimes_HealthStateIsWarning()
    {
        var registry = CreateRegistry();
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        for (int i = 0; i < 3; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        var dto = registry.GetOne("TestWorker");
        dto.HealthState.Should().Be(WorkerHealthState.Warning);
        dto.State.Should().Be(WorkerState.Warning);
    }

    // Test 4: RecordTickFailed 5 times => HealthState = Failed
    [Fact]
    public void RecordTickFailed_FiveTimes_HealthStateIsFailed()
    {
        var registry = CreateRegistry();
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        for (int i = 0; i < 5; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        var dto = registry.GetOne("TestWorker");
        dto.HealthState.Should().Be(WorkerHealthState.Failed);
        dto.State.Should().Be(WorkerState.Failed);
    }

    // Test 5: Never tick after 2x interval => HealthState = Warning
    [Fact]
    public void NeverTicked_After2xInterval_HealthStateIsWarning()
    {
        // With interval=0: 2x=0, so (now - registeredAt) > 0 => Warning immediately
        var registry = CreateRegistry();
        registry.Register("NeverStartedWorker", TimeSpan.Zero);

        var dto = registry.GetOne("NeverStartedWorker");
        dto.HealthState.Should().Be(WorkerHealthState.Warning);
    }

    // Test 6: GetAll returns all registered workers
    [Fact]
    public void GetAll_ReturnsAllRegisteredWorkers()
    {
        var registry = CreateRegistry();
        registry.Register("WorkerA", TimeSpan.FromSeconds(10));
        registry.Register("WorkerB", TimeSpan.FromSeconds(20));
        registry.Register("WorkerC", TimeSpan.FromSeconds(30));

        var all = registry.GetAll();
        all.Should().HaveCount(3);
        all.Should().Contain(w => w.WorkerName == "WorkerA");
        all.Should().Contain(w => w.WorkerName == "WorkerB");
        all.Should().Contain(w => w.WorkerName == "WorkerC");
    }

    // Test 7: Rolling history capped at 100 ticks
    [Fact]
    public void RecentTicks_CappedAt100()
    {
        var registry = CreateRegistry();
        registry.Register("TestWorker", TimeSpan.FromSeconds(5));

        for (int i = 0; i < 150; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickComplete("TestWorker");
        }

        var dto = registry.GetOne("TestWorker");
        dto.RecentTicks.Should().HaveCount(100);
    }

    // Test 8: State transition fires WorkerStatusChangedEvent
    [Fact]
    public async Task RecordTickFailed_TransitionToWarning_PublishesEvent()
    {
        var publisherMock = new Mock<IPublisher>();
        publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IPublisher)))
            .Returns(publisherMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var registry = new WorkerStatusRegistry(scopeFactoryMock.Object, NullLogger<WorkerStatusRegistry>.Instance);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        // Trigger 3 failures to cross Healthy -> Warning threshold
        for (int i = 0; i < 3; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        // Allow fire-and-forget tasks to complete
        await Task.Delay(100);

        publisherMock.Verify(p => p.Publish(
            It.Is<WorkerStatusChangedEvent>(e =>
                e.WorkerName == "TestWorker" &&
                e.NewState == WorkerHealthState.Warning),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
