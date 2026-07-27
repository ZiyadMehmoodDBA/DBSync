using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;
using Xunit;

namespace MSOSync.CliTests.Commands;

public sealed class PluginInstallCommandTests
{
    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body = "")
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return new MsoSyncHttpClient(http);
    }

    // ── ID / version parsing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.myrouter",       "acme.myrouter", null)]
    [InlineData("acme.myrouter@1.2.0", "acme.myrouter", "1.2.0")]
    [InlineData("acme.myrouter@2.0.0-beta", "acme.myrouter", "2.0.0-beta")]
    public void ParseIdVersion_ReturnsCorrectParts(string input, string expectedId, string? expectedVersion)
    {
        (string id, string? version) = PluginInstallCommand.ParseIdVersion(input);
        Assert.Equal(expectedId,      id);
        Assert.Equal(expectedVersion, version);
    }

    // ── HTTP status mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_On200()
    {
        using MsoSyncHttpClient client = BuildClient(
            HttpStatusCode.OK, """{"pluginId":"acme.myrouter","status":"Running"}""");
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter@1.0.0", "http://localhost:5000", null, client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_On202()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Accepted);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On400()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.BadRequest, "Invalid plugin ID");
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", "bad", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On404()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.NotFound);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.noexist@9.9.9", "http://localhost:5000", null, client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On409()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Conflict);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(1, exitCode);
    }
}
