using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class LifecycleRequestValidatorTests
{
    [Fact]
    public void MaintenanceStart_EmptyReason_Fails()
        => new MaintenanceStartRequestValidator()
            .Validate(new MaintenanceStartRequest("", null, false)).IsValid.Should().BeFalse();

    [Fact]
    public void MaintenanceStart_WithReason_Passes()
        => new MaintenanceStartRequestValidator()
            .Validate(new MaintenanceStartRequest("patching", null, true)).IsValid.Should().BeTrue();

    [Fact]
    public void Decommission_EmptyReason_Fails()
        => new DecommissionRequestValidator()
            .Validate(new DecommissionRequest("", null)).IsValid.Should().BeFalse();

    [Fact]
    public void Decommission_NegativeGrace_Fails()
        => new DecommissionRequestValidator()
            .Validate(new DecommissionRequest("site closure", -5)).IsValid.Should().BeFalse();

    [Fact]
    public void Activate_MissingFields_Fails()
        => new ActivateRequestValidator()
            .Validate(new ActivateRequest("", "", "")).IsValid.Should().BeFalse();
}
