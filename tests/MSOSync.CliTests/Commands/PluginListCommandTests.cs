using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;
using Xunit;

namespace MSOSync.CliTests.Commands;

public sealed class PluginListCommandTests
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

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsPlugins()
    {
        string responseJson = """
            [
              {"id":"acme.router","name":"My Router","version":"1.0.0","status":"Running","author":"Acme"}
            ]
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, responseJson);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsEmptyList()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, "[]");
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsMultiplePlugins()
    {
        string responseJson = """
            [
              {"id":"acme.router",     "name":"My Router",     "version":"1.0.0","status":"Running","author":"Acme"},
              {"id":"msosync.sqlserver","name":"SQL Collector", "version":"2.1.0","status":"Running","author":"MSOSync"},
              {"id":"legacy.plugin",   "name":"Old Plugin",    "version":"0.9.0","status":"Stopped","author":"Unknown"}
            ]
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, responseJson);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenServerReturns401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: "bad", client);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenServerReturns500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(1, exitCode);
    }
}
