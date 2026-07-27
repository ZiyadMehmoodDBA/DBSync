using System.CommandLine;
using MSOSync.Cli.Config;
using MSOSync.Cli.Packaging;

namespace MSOSync.Cli.Commands;

public sealed class PluginPackCommand
{
    public Command Build()
    {
        var outputOpt = new Option<string>("--output",
            () => "artifacts", "Directory where .msopkg is written");
        var configOpt = new Option<string>("--configuration",
            () => "Release", "MSBuild configuration (Release or Debug)");
        var signKeyOpt = new Option<string?>("--sign-key",
            "Path to .snk key file for strong-name signing");

        var cmd = new Command("pack", "Compile and pack the plugin into a .msopkg archive");
        cmd.AddOption(outputOpt);
        cmd.AddOption(configOpt);
        cmd.AddOption(signKeyOpt);

        cmd.SetHandler(async (output, configuration, signKey) =>
        {
            CliConfig config       = CliConfigStore.Load();
            string?   effectiveKey = signKey ?? (string.IsNullOrEmpty(config.SigningKeyPath)
                ? null : config.SigningKeyPath);

            int exitCode = await ExecuteAsync(
                Directory.GetCurrentDirectory(), output, configuration, effectiveKey);
            Environment.Exit(exitCode);
        }, outputOpt, configOpt, signKeyOpt);

        return cmd;
    }

    /// <summary>Testable entry point. Returns exit code.</summary>
    public Task<int> ExecuteAsync(
        string workingDir,
        string outputDir,
        string configuration,
        string? signingKeyPath,
        CancellationToken ct = default)
        => PluginPacker.PackAsync(workingDir, outputDir, configuration, signingKeyPath, ct);
}
