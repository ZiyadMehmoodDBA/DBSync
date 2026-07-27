using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginListCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var serverOpt = new Option<string?>("--server", "Base URL of the MSOSync server");
        var tokenOpt  = new Option<string?>("--token",  "JWT bearer token");

        var cmd = new Command("list", "List all plugins installed on a running MSOSync server");
        cmd.AddOption(serverOpt);
        cmd.AddOption(tokenOpt);

        cmd.SetHandler(async (server, token) =>
        {
            CliConfig config       = CliConfigStore.Load();
            string effectiveServer = server ?? config.ServerUrl;
            string? effectiveToken = token ?? (string.IsNullOrEmpty(config.ServerToken) ? null : config.ServerToken);

            int exitCode = await ExecuteAsync(effectiveServer, effectiveToken);
            Environment.Exit(exitCode);
        }, serverOpt, tokenOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            serverUrl,
        string?           token,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl, token);

        try
        {
            using HttpResponseMessage response =
                await client.GetRawAsync("/api/v1/plugins", ct);

            switch ((int)response.StatusCode)
            {
                case 200:
                    string json = await response.Content.ReadAsStringAsync(ct);
                    var    list = JsonSerializer.Deserialize<List<PluginListItem>>(json, JsonOptions)
                                  ?? [];
                    RenderTable(list, serverUrl);
                    return 0;

                case 401:
                    CliConsole.Error("Authentication failed — check --token");
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

    private static void RenderTable(List<PluginListItem> plugins, string serverUrl)
    {
        if (plugins.Count == 0)
        {
            CliConsole.Info("No plugins installed.");
            CliConsole.Info(string.Empty);
            CliConsole.Info($"0 plugin(s) installed on {serverUrl}");
            return;
        }

        string[] headers = ["ID", "NAME", "VERSION", "STATUS", "AUTHOR"];
        IEnumerable<string[]> rows = plugins.Select(p => new[]
        {
            p.Id          ?? string.Empty,
            p.Name        ?? string.Empty,
            p.Version     ?? string.Empty,
            p.Status      ?? string.Empty,
            p.Author      ?? string.Empty
        });

        CliConsole.Table(headers, rows);
        CliConsole.Info(string.Empty);
        CliConsole.Info($"{plugins.Count} plugin(s) installed on {serverUrl}");
    }

    private sealed record PluginListItem
    {
        [JsonPropertyName("id")]          public string? Id          { get; init; }
        [JsonPropertyName("name")]        public string? Name        { get; init; }
        [JsonPropertyName("version")]     public string? Version     { get; init; }
        [JsonPropertyName("status")]      public string? Status      { get; init; }
        [JsonPropertyName("author")]      public string? Author      { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
    }
}
