using System.CommandLine;
using MSOSync.Cli.Output;
using MSOSync.Cli.Scaffolding;

namespace MSOSync.Cli.Commands;

public sealed class PluginNewCommand
{
    public Command Build()
    {
        var nameArg   = new Argument<string>("name",
            "Plugin identifier in reverse-DNS format (e.g. acme.myrouter)");
        var outputOpt = new Option<string?>("--output",
            "Target directory to create the project in (default: ./<name>)");
        var authorOpt = new Option<string>("--author",
            () => string.Empty, "Author string written into plugin.json");
        var descOpt   = new Option<string>("--description",
            () => string.Empty, "Description written into plugin.json");

        var cmd = new Command("new", "Scaffold a new plugin project directory");
        cmd.AddArgument(nameArg);
        cmd.AddOption(outputOpt);
        cmd.AddOption(authorOpt);
        cmd.AddOption(descOpt);

        cmd.SetHandler(async (name, output, author, description) =>
        {
            int exitCode = await ExecuteAsync(name, output, author, description);
            Environment.Exit(exitCode);
        }, nameArg, outputOpt, authorOpt, descOpt);

        return cmd;
    }

    /// <summary>Testable entry point. Returns exit code.</summary>
    public Task<int> ExecuteAsync(
        string name,
        string? output,
        string author,
        string description,
        CancellationToken ct = default)
    {
        string targetDir = output ?? Path.Combine(Directory.GetCurrentDirectory(), name);
        int    result    = PluginScaffolder.Scaffold(name, targetDir, author, description);
        return Task.FromResult(result);
    }
}
