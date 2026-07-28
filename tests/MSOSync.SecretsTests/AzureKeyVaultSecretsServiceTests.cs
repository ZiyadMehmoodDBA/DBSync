using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

public sealed class AzureKeyVaultSecretsServiceTests
{
    private static AzureKeyVaultSecretsService Build(SecretClient client)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new AzureKeyVaultSecretsService(client, cache,
            new AzureKeyVaultOptions { VaultUri = "https://test.vault.azure.net/", CacheTtlSeconds = 60 });
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenSecretExists()
    {
        var mock = new Mock<SecretClient>();
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("Jwt--SigningKey"), "my-jwt-secret");
        mock.Setup(c => c.GetSecretAsync("Jwt--SigningKey", null, default))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var svc = Build(mock.Object);
        var result = await svc.GetSecretAsync("Jwt:SigningKey");

        result.Should().Be("my-jwt-secret");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenSecretNotFound()
    {
        var mock = new Mock<SecretClient>();
        mock.Setup(c => c.GetSecretAsync(It.IsAny<string>(), null, default))
            .ThrowsAsync(new RequestFailedException(404, "Secret not found"));

        var svc = Build(mock.Object);
        var result = await svc.GetSecretAsync("Missing:Key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsCachedValue_OnSecondCall()
    {
        var mock = new Mock<SecretClient>();
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("Cached--Key"), "cached-value");
        mock.Setup(c => c.GetSecretAsync("Cached--Key", null, default))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

        var svc = Build(mock.Object);
        await svc.GetSecretAsync("Cached:Key");
        await svc.GetSecretAsync("Cached:Key");

        // SecretClient called only once; second call served from cache
        mock.Verify(c => c.GetSecretAsync("Cached--Key", null, default), Times.Once);
    }

    [Fact]
    public async Task GetSecretAsync_MapsColonToDash_InKeyName()
    {
        var mock = new Mock<SecretClient>();
        mock.Setup(c => c.GetSecretAsync("Jwt--SigningKey", null, default))
            .ReturnsAsync(Response.FromValue(
                SecretModelFactory.KeyVaultSecret(new SecretProperties("Jwt--SigningKey"), "val"),
                Mock.Of<Response>()));

        var svc = Build(mock.Object);
        await svc.GetSecretAsync("Jwt:SigningKey");

        mock.Verify(c => c.GetSecretAsync("Jwt--SigningKey", null, default), Times.Once);
    }
}
