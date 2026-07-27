using System.Diagnostics;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Packaging;

public static class PackageSigningService
{
    /// <summary>
    /// Signs <paramref name="assemblyPath"/> with <paramref name="keyPath"/> using `dotnet sn -R`.
    /// Returns true on success (or when skipped because keyPath is empty).
    /// Returns false if signing fails.
    /// </summary>
    public static bool TrySign(string assemblyPath, string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            CliConsole.Warn("No signing key configured — package is unsigned");
            return true;
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList          = { "sn", "-R", assemblyPath, keyPath },
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            CliConsole.Ok($"Signed: {Path.GetFileName(assemblyPath)} ({Path.GetFileName(keyPath)})");
            return true;
        }

        string err = proc.StandardError.ReadToEnd();
        CliConsole.Error($"Signing failed: {err.Trim()}");
        return false;
    }
}
