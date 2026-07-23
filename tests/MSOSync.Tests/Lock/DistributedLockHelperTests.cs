using FluentAssertions;
using Moq;
using MSOSync.Common.Locks;
using Xunit;

namespace MSOSync.Tests.Lock;

public sealed class DistributedLockHelperTests
{
    private readonly Mock<IDistributedLockService> _service = new();
    private readonly Mock<IDistributedLock>        _handle  = new();

    private static DistributedLockOptions Options(int retryCount = 2) => new()
    {
        DefaultExpiry = TimeSpan.FromSeconds(10),
        RetryCount    = retryCount,
        RetryDelay    = TimeSpan.Zero   // no delay in tests
    };

    [Fact]
    public async Task Returns_handle_on_first_attempt()
    {
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_handle.Object);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(), CancellationToken.None);

        result.Should().NotBeNull();
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Returns_handle_on_second_attempt()
    {
        var callCount = 0;
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 2 ? _handle.Object : null);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(retryCount: 2), CancellationToken.None);

        result.Should().NotBeNull();
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Returns_null_when_all_attempts_fail()
    {
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(retryCount: 2), CancellationToken.None);

        result.Should().BeNull();
        // retryCount=2 means attempt 0, 1, 2 → 3 total calls
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}
