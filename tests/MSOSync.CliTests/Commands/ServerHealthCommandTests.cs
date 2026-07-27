using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;
using Xunit;

namespace MSOSync.CliTests.Commands;

public sealed class ServerHealthCommandTests
{
    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return new MsoSyncHttpClient(http);
    }

    // ── Healthy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenStatusIsHealthy()
    {
        string body = """
            {
              "status": "Healthy",
              "results": {
                "database": { "status": "Healthy" },
                "plugins":  { "status": "Healthy" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(0, exitCode);
    }

    // ── Degraded ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenStatusIsDegraded()
    {
        string body = """
            {
              "status": "Degraded",
              "results": {
                "database": { "status": "Healthy" },
                "plugins":  { "status": "Degraded", "description": "1 plugin in Failed state" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(0, exitCode);
    }

    // ── Unhealthy ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenStatusIsUnhealthy()
    {
        string body = """
            {
              "status": "Unhealthy",
              "results": {
                "database": { "status": "Unhealthy", "description": "connection timeout" },
                "plugins":  { "status": "Healthy" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.ServiceUnavailable, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_When200ButStatusIsUnhealthy()
    {
        // HTTP 200 but body status = Unhealthy → exit 1
        string body = """{"status": "Unhealthy", "results": {}}""";
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Connection refused ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenConnectionRefused()
    {
        // FakeHttpMessageHandler that throws HttpRequestException
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var http    = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999") };
        using MsoSyncHttpClient client = new(http);

        var cmd = new ServerHealthCommand();
        int exitCode = await cmd.ExecuteAsync("http://localhost:9999", client);

        Assert.Equal(1, exitCode);
    }

    // ── Unexpected HTTP status ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenNon200Non503Received()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Found, string.Empty);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Malformed JSON ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenResponseJsonIsMalformed()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, "{ not valid json }");
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Case-insensitive status matching ─────────────────────────────────────

    [Theory]
    [InlineData("healthy",   0)]
    [InlineData("HEALTHY",   0)]
    [InlineData("Healthy",   0)]
    [InlineData("degraded",  0)]
    [InlineData("DEGRADED",  0)]
    [InlineData("unhealthy", 1)]
    [InlineData("UNHEALTHY", 1)]
    public async Task ExecuteAsync_HandlesStatusCaseInsensitively(string status, int expectedExitCode)
    {
        string body = $$$"""{"status": "{{{status}}}", "results": {}}""";
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(expectedExitCode, exitCode);
    }
}
