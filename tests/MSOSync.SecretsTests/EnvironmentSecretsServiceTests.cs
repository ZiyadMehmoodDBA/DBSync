using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

[CollectionDefinition("SecretsEnvVars", DisableParallelization = true)]
public sealed class SecretsEnvVarsCollection { }

[Collection("SecretsEnvVars")]
public sealed class EnvironmentSecretsServiceTests : IDisposable
{
    private const string TestEnvVar = "MSOSYNC_TEST_SECRETS_KEY__VALUE";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, null);
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_KEY__VALUE", null);
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_SECRETS_KEY__VALUE", "test-secret");
        var svc = Build();

        var result = await svc.GetSecretAsync("MSOSYNC_TEST_SECRETS_KEY:VALUE");

        result.Should().Be("test-secret");
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenEnvVarNotSet()
    {
        var svc = Build();

        var result = await svc.GetSecretAsync("NONEXISTENT_KEY");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_FallsBackToConfiguration_InDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SomeKey"] = "from-config" })
            .Build();
        var svc = new EnvironmentSecretsService(config, isProduction: false);

        var result = await svc.GetSecretAsync("SomeKey");

        result.Should().Be("from-config");
    }

    [Fact]
    public async Task GetSecretAsync_DoesNotFallBackToConfig_InProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SomeKey"] = "from-config" })
            .Build();
        var svc = new EnvironmentSecretsService(config, isProduction: true);

        var result = await svc.GetSecretAsync("SomeKey");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_KEY__VALUE", "anything");
        var svc = Build();

        var exists = await svc.ExistsAsync("MSOSYNC_TEST_KEY:VALUE");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetSecretBytesAsync_ReturnsUtf8Bytes_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("MSOSYNC_TEST_SECRETS_KEY__VALUE", "bytes-value");
        var svc = Build();

        var bytes = await svc.GetSecretBytesAsync("MSOSYNC_TEST_SECRETS_KEY:VALUE");

        bytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes("bytes-value"));
    }

    private static EnvironmentSecretsService Build() =>
        new(new ConfigurationBuilder().Build(), isProduction: true);
}
