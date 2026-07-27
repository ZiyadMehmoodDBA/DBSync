using System.CommandLine;
using MSOSync.Cli.Commands;

// Root command
var rootCommand = new RootCommand("MSOSync CLI — plugin scaffolding, packaging, and server management");

// plugin sub-tree
var pluginCommand = new Command("plugin", "Manage MSOSync plugins");
pluginCommand.AddCommand(new PluginNewCommand().Build());
pluginCommand.AddCommand(new PluginPackCommand().Build());
pluginCommand.AddCommand(new PluginPublishCommand().Build());
pluginCommand.AddCommand(new PluginInstallCommand().Build());
pluginCommand.AddCommand(new PluginListCommand().Build());
rootCommand.AddCommand(pluginCommand);

// server sub-tree
var serverCommand = new Command("server", "Interact with a running MSOSync server");
serverCommand.AddCommand(new ServerHealthCommand().Build());
rootCommand.AddCommand(serverCommand);

return await rootCommand.InvokeAsync(args);
