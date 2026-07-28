using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

[Collection("SecretsEnvVars")]
public sealed class SecretsIntegrationTests : IDisposable
{
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("INTEGRATION_TEST_KEY__SECRET", null);
    }

    [Fact]
    public async Task CompositeResolves_EnvVar_WhenSet()
    {
        Environment.SetEnvironmentVariable("INTEGRATION_TEST_KEY__SECRET", "env-value");
        var envSvc = new EnvironmentSecretsService(new ConfigurationBuilder().Build(), isProduction: true);
        var composite = new CompositeSecretsService([envSvc]);

        var result = await composite.GetSecretAsync("INTEGRATION_TEST_KEY:SECRET");

        result.Should().Be("env-value");
    }

    [Fact]
    public async Task Composite_ReturnsNull_WhenKeyMissing()
    {
        var envSvc = new EnvironmentSecretsService(new ConfigurationBuilder().Build(), isProduction: true);
        var composite = new CompositeSecretsService([envSvc]);

        var result = await composite.GetSecretAsync("DEFINITELY_MISSING_KEY_XYZ");

        result.Should().BeNull();
    }
}
