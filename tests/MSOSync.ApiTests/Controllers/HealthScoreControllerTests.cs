using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Health;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class HealthScoreControllerTests
{
    private readonly Mock<IHealthScoringService> _svc = new();
    private readonly HealthScoreController _controller;

    public HealthScoreControllerTests()
        => _controller = new HealthScoreController(_svc.Object);

    [Fact]
    public async Task GetScores_ReturnsOkWithScores()
    {
        var score = new NodeHealthScore("node-1", "Node A", 95, "A", 40, 30, 20, 5, DateTime.UtcNow);
        _svc.Setup(s => s.GetScoresAsync(default))
            .ReturnsAsync(new List<NodeHealthScore> { score });

        var result = await _controller.GetScores();

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = (IEnumerable<NodeHealthScore>)((OkObjectResult)result.Result!).Value!;
        body.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetScore_ReturnsOk_WhenNodeExists()
    {
        var score = new NodeHealthScore("node-1", "Node A", 95, "A", 40, 30, 20, 5, DateTime.UtcNow);
        _svc.Setup(s => s.GetScoreAsync("node-1", default)).ReturnsAsync(score);

        var result = await _controller.GetScore("node-1");

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = (NodeHealthScore)((OkObjectResult)result.Result!).Value!;
        body.NodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task GetScore_ReturnsNotFound_WhenNodeMissing()
    {
        _svc.Setup(s => s.GetScoreAsync("missing", default)).ReturnsAsync((NodeHealthScore?)null);

        var result = await _controller.GetScore("missing");

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
