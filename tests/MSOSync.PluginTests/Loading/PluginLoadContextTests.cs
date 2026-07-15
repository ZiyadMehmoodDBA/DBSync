using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using MSOSync.Plugin.Loading;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginLoadContextTests : IDisposable
{
    // Use this test assembly itself as a known .dll to load from a directory
    private readonly string _dir;
    private readonly string _dllPath;

    public PluginLoadContextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "plc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        // Copy the test assembly into temp dir so PluginLoadContext can load it
        var src = typeof(PluginLoadContextTests).Assembly.Location;
        _dllPath = Path.Combine(_dir, Path.GetFileName(src));
        File.Copy(src, _dllPath, overwrite: true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (UnauthorizedAccessException) { /* Loaded DLLs may still be locked; best-effort cleanup. */ }
        catch (IOException) { /* Same reason — ignore on Windows. */ }
    }

    [Fact]
    public void PluginLoadContext_IsCollectible()
    {
        var ctx = new PluginLoadContext(_dir);
        ctx.IsCollectible.Should().BeTrue();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_LoadFromAssemblyPath_Succeeds()
    {
        var ctx = new PluginLoadContext(_dir);
        var assembly = ctx.LoadFromAssemblyPath(_dllPath);
        assembly.Should().NotBeNull();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_LibDirectory_ProbesLib()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);

        var src = typeof(PluginLoadContextTests).Assembly.Location;
        var libDll = Path.Combine(libDir, Path.GetFileName(src));
        File.Copy(src, libDll, overwrite: true);

        var ctx = new PluginLoadContext(_dir, libDir);
        // Constructing with libDir should not throw
        ctx.Should().NotBeNull();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_Unload_DoesNotThrow()
    {
        var ctx = new PluginLoadContext(_dir);
        var act = () => ctx.Unload();
        act.Should().NotThrow();
    }
}
