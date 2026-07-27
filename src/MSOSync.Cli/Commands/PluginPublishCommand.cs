using System.CommandLine;
using System.Net;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginPublishCommand
{
    public Command Build()
    {
        var fileArg      = new Argument<string>("file", "Path to the .msopkg file to publish");
        var registryOpt  = new Option<string?>("--registry", "Base URL of the marketplace registry");
        var apiKeyOpt    = new Option<string?>("--api-key",  "API key for registry authentication");

        var cmd = new Command("publish", "Upload a .msopkg file to a marketplace registry");
        cmd.AddArgument(fileArg);
        cmd.AddOption(registryOpt);
        cmd.AddOption(apiKeyOpt);

        cmd.SetHandler(async (file, registry, apiKey) =>
        {
            CliConfig config = CliConfigStore.Load();
            string effectiveRegistry = registry
                ?? (string.IsNullOrEmpty(config.RegistryUrl) ? "https://marketplace.msosync.io" : config.RegistryUrl);
            string effectiveApiKey   = apiKey
                ?? config.RegistryApiKey;

            int exitCode = await ExecuteAsync(file, effectiveRegistry, effectiveApiKey);
            Environment.Exit(exitCode);
        }, fileArg, registryOpt, apiKeyOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string filePath,
        string registryUrl,
        string apiKey,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        if (!File.Exists(filePath))
        {
            CliConsole.Error($"File not found: {filePath}");
            return 2;
        }

        string fileName = Path.GetFileName(filePath);
        CliConsole.Info($"[OK]  Publishing {fileName} → {registryUrl}");

        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(registryUrl);

        try
        {
            using HttpResponseMessage response = await client.PostMultipartAsync(
                "/api/v1/packages", "package", filePath, ct);

            return HandlePublishResponse(response, fileName);
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

    private static int HandlePublishResponse(HttpResponseMessage response, string fileName)
    {
        switch ((int)response.StatusCode)
        {
            case 201:
                // id@version extracted from file name: strip .msopkg, last '-' splits id and version
                string baseName = Path.GetFileNameWithoutExtension(fileName); // e.g. acme.myrouter-1.0.0
                int    dashIdx  = baseName.LastIndexOf('-');
                string idVer    = dashIdx > 0
                    ? $"{baseName[..dashIdx]}@{baseName[(dashIdx + 1)..]}"
                    : baseName;

                CliConsole.Ok($"Published: {idVer}");
                CliConsole.Info($"     Registry: {response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Authority)}");
                CliConsole.Info($"     Install:  msosync plugin install {idVer}");
                return 0;

            case 400:
                string body400 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                CliConsole.Error(string.IsNullOrWhiteSpace(body400) ? "Bad request" : body400.Trim());
                return 2;

            case 401:
                CliConsole.Error("Authentication failed — check --api-key");
                return 1;

            case 409:
                CliConsole.Error("Version already exists on registry");
                return 1;

            default:
                if ((int)response.StatusCode >= 500)
                {
                    CliConsole.Error($"Registry server error: {(int)response.StatusCode}");
                    return 1;
                }
                CliConsole.Error($"Unexpected response: {(int)response.StatusCode}");
                return 1;
        }
    }
}
