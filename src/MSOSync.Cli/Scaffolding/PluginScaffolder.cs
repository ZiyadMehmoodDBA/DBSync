using System.Reflection;
using System.Text.RegularExpressions;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Scaffolding;

public static class PluginScaffolder
{
    private static readonly Regex IdPattern =
        new(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)*$", RegexOptions.Compiled);

    /// <summary>Validates the plugin ID format.</summary>
    public static bool IsValidId(string id) => IdPattern.IsMatch(id);

    /// <summary>
    /// Derives assembly name and class name from a plugin ID.
    /// e.g. "acme.my-router" → ("Acme.MyRouter", "MyRouterPlugin")
    /// </summary>
    public static (string AssemblyName, string ClassName) DeriveNames(string pluginId)
    {
        // Pascal-case a single dot-segment: split on '-' and capitalise each word.
        static string PascalDotSegment(string seg) =>
            string.Join(string.Empty,
                seg.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

        string[] dotSegments = pluginId.Split('.');

        // Assembly name: each dot-segment PascalCased, joined with '.'
        string assemblyName = string.Join(".", dotSegments.Select(PascalDotSegment));

        // Class name: last dot-segment PascalCased + "Plugin"
        string className = PascalDotSegment(dotSegments[^1]) + "Plugin";

        return (assemblyName, className);
    }

    /// <summary>
    /// Scaffolds a new plugin project directory. Returns 0 on success, 1 or 2 on failure.
    /// </summary>
    public static int Scaffold(string pluginId, string outputDir, string author, string description)
    {
        if (!IsValidId(pluginId))
        {
            CliConsole.Error($"Plugin ID must match pattern: ^[a-z][a-z0-9]*(\\.[a-z][a-z0-9-]*)*$");
            CliConsole.Error($"Got: \"{pluginId}\"");
            return 2;
        }

        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            CliConsole.Error($"Target directory already exists and is non-empty: {outputDir}");
            return 1;
        }

        (string assemblyName, string className) = DeriveNames(pluginId);
        // Last dot-segment is the display name portion
        string[] dotParts  = pluginId.Split('.');
        string   nameParts = dotParts[^1];
        string   displayName = string.Join(" ", nameParts.Split('-')
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

        Directory.CreateDirectory(outputDir);

        var tokens = new Dictionary<string, string>
        {
            ["{{Id}}"]           = pluginId,
            ["{{Name}}"]         = displayName,
            ["{{AssemblyName}}"] = assemblyName,
            ["{{Namespace}}"]    = assemblyName,
            ["{{ClassName}}"]    = className,
            ["{{Author}}"]       = author,
            ["{{Description}}"]  = description
        };

        // Map: (embedded resource suffix → output file name)
        var templateMap = new[]
        {
            ("Plugin.csproj.template",          $"{assemblyName}.csproj"),
            ("PluginImpl.cs.template",           $"{className}.cs"),
            ("plugin.json.template",             "plugin.json"),
            ("plugin.config.json.template",      "plugin.config.json")
        };

        foreach ((string resourceSuffix, string outputFile) in templateMap)
        {
            string content = ReadTemplate(resourceSuffix);
            foreach ((string token, string value) in tokens)
                content = content.Replace(token, value);

            File.WriteAllText(Path.Combine(outputDir, outputFile), content);
        }

        CliConsole.Ok($"Created plugin project: {outputDir}/");
        CliConsole.Info($"     {outputDir}/{assemblyName}.csproj");
        CliConsole.Info($"     {outputDir}/{className}.cs");
        CliConsole.Info($"     {outputDir}/plugin.json");
        CliConsole.Info($"     {outputDir}/plugin.config.json");
        CliConsole.Info(string.Empty);
        CliConsole.Info("Next steps:");
        CliConsole.Info($"  cd {outputDir}");
        CliConsole.Info("  dotnet build");
        CliConsole.Info("  msosync plugin pack");

        return 0;
    }

    private static string ReadTemplate(string resourceSuffix)
    {
        Assembly asm  = typeof(PluginScaffolder).Assembly;
        // Resource names use namespace-style dots: MSOSync.Cli.Scaffolding.Templates.<suffix-with-dots-replaced-by-dots>
        // The embedded resource name mirrors the folder path using '.' separators
        string   name = asm.GetManifestResourceNames()
                           .Single(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        using Stream stream = asm.GetManifestResourceStream(name)!;
        using var   reader  = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
