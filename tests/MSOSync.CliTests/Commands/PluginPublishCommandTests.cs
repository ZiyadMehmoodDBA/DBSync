using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;
using Xunit;

namespace MSOSync.CliTests.Commands;

public sealed class PluginPublishCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pkgPath;

    public PluginPublishCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _pkgPath = Path.Combine(_tempDir, "acme.myrouter-1.0.0.msopkg");
        File.WriteAllBytes(_pkgPath, [0x50, 0x4B]); // minimal fake ZIP header
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body = "")
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://marketplace.msosync.io") };
        return new MsoSyncHttpClient(http);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_On201()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Created);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On400()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.BadRequest, "Version validation failed");
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "bad-key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On409()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Conflict);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_WhenFileDoesNotExist()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Created);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(
            "/non/existent/file.msopkg", "https://marketplace.msosync.io", "key", client);
        Assert.Equal(2, exitCode);
    }
}
