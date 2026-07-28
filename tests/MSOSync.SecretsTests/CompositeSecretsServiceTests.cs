using FluentAssertions;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class CompositeSecretsServiceTests
{
    [Fact]
    public async Task GetSecretAsync_ReturnsFirstNonNull()
    {
        var first = new StubSecretsService("key", null);
        var second = new StubSecretsService("key", "found");
        var composite = new CompositeSecretsService([first, second]);

        var result = await composite.GetSecretAsync("key");

        result.Should().Be("found");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenAllProvidersReturnNull()
    {
        var composite = new CompositeSecretsService([new StubSecretsService("key", null)]);

        var result = await composite.GetSecretAsync("other");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsFirst_WhenMultipleMatch()
    {
        var first = new StubSecretsService("key", "first-value");
        var second = new StubSecretsService("key", "second-value");
        var composite = new CompositeSecretsService([first, second]);

        var result = await composite.GetSecretAsync("key");

        result.Should().Be("first-value");
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenNotFound()
    {
        var composite = new CompositeSecretsService([new StubSecretsService("key", null)]);

        var exists = await composite.ExistsAsync("missing");

        exists.Should().BeFalse();
    }

    private sealed class StubSecretsService(string key, string? value) : ISecretsService
    {
        public Task<string?> GetSecretAsync(string k, CancellationToken ct = default)
            => Task.FromResult(k == key ? value : null);
        public Task<byte[]?> GetSecretBytesAsync(string k, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
        public Task<bool> ExistsAsync(string k, CancellationToken ct = default)
            => Task.FromResult(k == key && value is not null);
    }
}
