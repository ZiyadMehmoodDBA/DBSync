using System.CommandLine;
using System.Net;
using System.Text.Json;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginInstallCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var idArg      = new Argument<string>("id",
            "Plugin ID, optionally with @version (e.g. acme.myrouter@1.2.0)");
        var serverOpt  = new Option<string?>("--server", "Base URL of the MSOSync server");
        var tokenOpt   = new Option<string?>("--token",  "JWT bearer token for server authentication");

        var cmd = new Command("install", "Install a plugin on a running MSOSync server");
        cmd.AddArgument(idArg);
        cmd.AddOption(serverOpt);
        cmd.AddOption(tokenOpt);

        cmd.SetHandler(async (id, server, token) =>
        {
            CliConfig config        = CliConfigStore.Load();
            string effectiveServer  = server ?? config.ServerUrl;
            string? effectiveToken  = token ?? (string.IsNullOrEmpty(config.ServerToken) ? null : config.ServerToken);

            int exitCode = await ExecuteAsync(id, effectiveServer, effectiveToken);
            Environment.Exit(exitCode);
        }, idArg, serverOpt, tokenOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            idWithVersion,
        string            serverUrl,
        string?           token,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        (string pluginId, string? version) = ParseIdVersion(idWithVersion);

        CliConsole.Info($"[OK]  Installing {pluginId}{(version is not null ? "@" + version : string.Empty)} on {serverUrl}");

        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl, token);

        try
        {
            var body = new { pluginId, version };
            using HttpResponseMessage response =
                await client.PostJsonAsync("/api/v1/marketplace/plugins/install", body, ct);

            return HandleInstallResponse(response, pluginId, version);
        }
        catch (HttpRequestException ex)
        {
            CliConsole.Error($"Network error: {ex.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            CliConsole.Error("Request timed out after 30 seconds");
            return 1;
        }
        catch (Exception ex)
        {
            CliConsole.Error($"Unexpected error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }

    /// <summary>
    /// Parses "acme.myrouter@1.2.0" → ("acme.myrouter", "1.2.0")
    /// and  "acme.myrouter"         → ("acme.myrouter", null)
    /// </summary>
    public static (string Id, string? Version) ParseIdVersion(string input)
    {
        int atIndex = input.IndexOf('@');
        if (atIndex < 0)
            return (input, null);
        return (input[..atIndex], input[(atIndex + 1)..]);
    }

    private static int HandleInstallResponse(
        HttpResponseMessage response, string pluginId, string? version)
    {
        string idVer = version is not null ? $"{pluginId}@{version}" : pluginId;

        switch ((int)response.StatusCode)
        {
            case 200:
                string body200 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string status  = "Installed";
                try
                {
                    using var doc = JsonDocument.Parse(body200);
                    if (doc.RootElement.TryGetProperty("status", out var statusEl))
                        status = statusEl.GetString() ?? status;
                }
                catch { /* ignore parse error — print generic success */ }

                CliConsole.Ok($"Installed: {idVer}");
                CliConsole.Info($"     Status: {status}");
                return 0;

            case 202:
                CliConsole.Ok($"Install queued — check status with: msosync plugin list");
                return 0;

            case 400:
                string body400 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                CliConsole.Error(string.IsNullOrWhiteSpace(body400) ? "Bad request" : body400.Trim());
                return 2;

            case 401:
                CliConsole.Error("Authentication failed — check --token");
                return 1;

            case 404:
                CliConsole.Error($"Plugin not found: {idVer}");
                return 2;

            case 409:
                CliConsole.Error("Plugin already installed");
                return 1;

            default:
                if ((int)response.StatusCode >= 500)
                {
                    CliConsole.Error($"Server error: {(int)response.StatusCode}");
                    return 1;
                }
                CliConsole.Error($"Unexpected response: {(int)response.StatusCode}");
                return 1;
        }
    }
}
