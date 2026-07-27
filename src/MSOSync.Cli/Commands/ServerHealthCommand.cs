using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class ServerHealthCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var serverOpt = new Option<string?>("--server", "Base URL of the MSOSync server");

        var cmd = new Command("health", "Check the health of a running MSOSync server");
        cmd.AddOption(serverOpt);

        cmd.SetHandler(async (server) =>
        {
            CliConfig config       = CliConfigStore.Load();
            string effectiveServer = server ?? config.ServerUrl;

            int exitCode = await ExecuteAsync(effectiveServer);
            Environment.Exit(exitCode);
        }, serverOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            serverUrl,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl);

        try
        {
            using HttpResponseMessage response =
                await client.GetRawAsync("/health", ct);

            int statusCode = (int)response.StatusCode;

            if (statusCode != 200 && statusCode != 503)
            {
                CliConsole.Error($"Unexpected response: {statusCode}");
                return 1;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            HealthResponse? health;
            try
            {
                health = JsonSerializer.Deserialize<HealthResponse>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                CliConsole.Error($"Response parse error: {ex.Message}");
                return 1;
            }

            if (health is null)
            {
                CliConsole.Error("Empty or null health response");
                return 1;
            }

            return RenderHealth(serverUrl, health, statusCode);
        }
        catch (HttpRequestException)
        {
            CliConsole.Error($"Cannot reach server at {serverUrl}");
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

    private static int RenderHealth(string serverUrl, HealthResponse health, int httpStatus)
    {
        string overallStatus = health.Status ?? "Unknown";

        switch (overallStatus.ToUpperInvariant())
        {
            case "HEALTHY":
                CliConsole.Ok($"Server: {serverUrl}");
                CliConsole.Info($"     Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 0;

            case "DEGRADED":
                CliConsole.Warn($"Server: {serverUrl}");
                CliConsole.Info($"      Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 0;

            case "UNHEALTHY":
            default:
                CliConsole.Error($"Server: {serverUrl}");
                CliConsole.Error($"      Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 1;
        }
    }

    private static void RenderChecks(Dictionary<string, HealthCheckEntry>? results)
    {
        if (results is null) return;
        foreach ((string name, HealthCheckEntry entry) in results)
        {
            string line = string.IsNullOrWhiteSpace(entry.Description)
                ? $"     {name}: {entry.Status}"
                : $"     {name}: {entry.Status} — {entry.Description}";
            CliConsole.Info(line);
        }
    }

    // ── DTOs (internal — not part of public API) ─────────────────────────────

    private sealed record HealthResponse
    {
        [JsonPropertyName("status")]  public string?                               Status  { get; init; }
        [JsonPropertyName("results")] public Dictionary<string, HealthCheckEntry>? Results { get; init; }
    }

    private sealed record HealthCheckEntry
    {
        [JsonPropertyName("status")]      public string? Status      { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
    }
}
