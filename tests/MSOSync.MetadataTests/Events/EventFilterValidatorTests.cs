using FluentAssertions;
using MSOSync.Metadata.Events;
using Xunit;

namespace MSOSync.MetadataTests.Events;

public sealed class EventFilterValidatorTests
{
    private static EventFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        var result = Sut().Validate(new EventFilter { Page = 0 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        var result = Sut().Validate(new EventFilter { PageSize = 101 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public void PageSize_Zero_Fails()
    {
        var result = Sut().Validate(new EventFilter { PageSize = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        var result = Sut().Validate(new EventFilter
        {
            From = now.AddHours(1),
            To   = now
        });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To");
    }

    [Fact]
    public void Valid_Defaults_Pass()
    {
        var result = Sut().Validate(new EventFilter());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_WithAll_Pass()
    {
        var now = DateTime.UtcNow;
        var result = Sut().Validate(new EventFilter
        {
            SourceNodeId = "node-1",
            TriggerId    = "trig-1",
            EventType    = 'I',
            IsProcessed  = false,
            From         = now.AddDays(-1),
            To           = now,
            Page         = 2,
            PageSize     = 100
        });
        result.IsValid.Should().BeTrue();
    }
}
