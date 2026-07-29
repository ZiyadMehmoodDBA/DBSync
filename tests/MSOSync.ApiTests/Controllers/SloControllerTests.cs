using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Health;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class SloControllerTests
{
    private readonly Mock<ISloService> _svc = new();
    private readonly SloController _controller;

    public SloControllerTests() => _controller = new SloController(_svc.Object);

    [Fact]
    public async Task GetStatus_ReturnsOkWithSloStatus()
    {
        var now = DateTime.UtcNow;
        var status = new SloStatus(1.0, 0.999, true, 1200, 5000, true, now.AddHours(-24), now);
        _svc.Setup(s => s.GetStatusAsync(default)).ReturnsAsync(status);

        var result = await _controller.GetStatus();

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = (SloStatus)((OkObjectResult)result.Result!).Value!;
        body.DeliveryRateMet.Should().BeTrue();
        body.LatencyP99Met.Should().BeTrue();
    }
}
