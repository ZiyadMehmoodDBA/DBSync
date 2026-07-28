// tests/MSOSync.MetricsTests/PipelineActivitySourceTests.cs
using System.Diagnostics;
using FluentAssertions;
using MSOSync.Metrics;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class PipelineActivitySourceTests : IDisposable
{
    private readonly ActivityListener _listener = new();
    private readonly List<Activity> _completed = [];

    public PipelineActivitySourceTests()
    {
        _listener.ShouldListenTo = source => source.Name == "MSOSync.Pipeline";
        _listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllDataAndRecorded;
        _listener.ActivityStopped = activity => _completed.Add(activity);
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Source_HasCorrectName()
    {
        PipelineActivitySource.Source.Name.Should().Be("MSOSync.Pipeline");
    }

    [Fact]
    public void StartActivity_SyncCycle_IsNotNull_WhenListenerRegistered()
    {
        using var activity = PipelineActivitySource.Source.StartActivity("sync.cycle");
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("sync.cycle");
    }

    [Fact]
    public void StartActivity_SyncDispatch_IncludesNodeIdTag()
    {
        using var dispatch = PipelineActivitySource.Source.StartActivity("sync.dispatch");
        dispatch?.SetTag("node.id", "node-42");

        dispatch.Should().NotBeNull();
        dispatch!.Tags.Should().Contain(t => t.Key == "node.id" && t.Value == "node-42");
    }

    [Fact]
    public void StartActivity_SyncDispatch_IsChildOf_SyncCycle()
    {
        using var cycle = PipelineActivitySource.Source.StartActivity("sync.cycle");
        using var dispatch = PipelineActivitySource.Source.StartActivity("sync.dispatch");

        cycle.Should().NotBeNull();
        dispatch.Should().NotBeNull();
        dispatch!.ParentId.Should().Be(cycle!.Id);
    }

    [Fact]
    public void StartActivity_ReturnsNull_WhenSourceHasNoListener()
    {
        // An isolated source with no registered listener returns null —
        // this is the safe no-op behavior callers rely on when OTel is disabled.
        using var isolated = new ActivitySource("MSOSync.Pipeline.IsolatedTest");
        using var activity = isolated.StartActivity("sync.cycle");

        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_SyncSend_IncludesHttpStatusTag()
    {
        using var send = PipelineActivitySource.Source.StartActivity("sync.send");
        send?.SetTag("http.status_code", "200");

        send.Should().NotBeNull();
        send!.Tags.Should().Contain(t => t.Key == "http.status_code" && t.Value == "200");
    }

    [Fact]
    public void StartActivity_CompletedActivity_IsRecordedByListener()
    {
        using (var activity = PipelineActivitySource.Source.StartActivity("sync.ack"))
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        _completed.Should().ContainSingle(a => a.OperationName == "sync.ack"
            && a.Status == ActivityStatusCode.Ok);
    }
}
