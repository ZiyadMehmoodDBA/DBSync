using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Sdk.Hosting;
using System.Reflection;
using Xunit;
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostEnvironment;

namespace MSOSync.PluginTests.Lifecycle;

public sealed class PluginActivatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly PluginRegistry _registry = new();

    public PluginActivatorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    // Fake plugin class that implements IPlugin via PluginBase
    public sealed class FakePlugin : PluginBase { }

    // Fake plugin with no parameterless constructor
    public sealed class NoCtorPlugin : PluginBase
    {
#pragma warning disable CS8618
        public NoCtorPlugin(string _) { }
#pragma warning restore CS8618
    }

    // Fake plugin that doesn't implement IPlugin
    public sealed class NotAPlugin { }

    private PluginActivator MakeActivator()
    {
        var hostEnv = new Mock<IHostEnvironment>();
        hostEnv.Setup(e => e.EnvironmentName).Returns("Development");
        hostEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);

        return new PluginActivator(
            _registry,
            NullLoggerFactory.Instance,
            hostEnv.Object,
            new ConfigurationBuilder().Build(),
            Options.Create(new PluginHostOptions()),
            NullLogger<PluginActivator>.Instance);
    }

    private void RegisterPlugin(string pluginId, Type entryType, Assembly assembly)
    {
        var manifest = new PluginManifest
        {
            Id = pluginId, Name = pluginId, Version = "1.0.0",
            SdkVersion = "1.0", ApiVersion = "1", StartupOrder = 1000,
            MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "fake.dll", EntryType = entryType.FullName!,
            Author = "Test", Description = "Test",
        };
        var descriptor = new PluginDescriptor
        {
            PluginId = pluginId, Name = pluginId, Version = "1.0.0",
            Status = PluginStatus.Loaded, LoadedAt = DateTime.UtcNow, Manifest = manifest,
        };
        _registry.Register(descriptor);
        var runtime      = _registry.GetRuntime(pluginId)!;
        runtime.Assembly = assembly;
    }

    [Fact]
    public async Task ActivateAsync_ValidPlugin_ReturnsTrue_SetsInstance()
    {
        RegisterPlugin("test", typeof(FakePlugin), typeof(FakePlugin).Assembly);
        var activator = MakeActivator();

        var result = await activator.ActivateAsync("test", default);

        result.Should().BeTrue();
        _registry.GetRuntime("test")!.Instance.Should().BeOfType<FakePlugin>();
    }

    [Fact]
    public async Task ActivateAsync_ValidPlugin_SetsContext()
    {
        RegisterPlugin("test", typeof(FakePlugin), typeof(FakePlugin).Assembly);
        var activator = MakeActivator();

        await activator.ActivateAsync("test", default);

        _registry.GetRuntime("test")!.Context.Should().NotBeNull();
    }

    [Fact]
    public async Task ActivateAsync_TypeNotInAssembly_ReturnsFalse_SetsDescriptorFailed()
    {
        var manifest = new PluginManifest
        {
            Id = "bad", Name = "bad", Version = "1.0.0",
            SdkVersion = "1.0", ApiVersion = "1", StartupOrder = 1000,
            MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "fake.dll", EntryType = "No.Such.Type",
            Author = "Test", Description = "Test",
        };
        var descriptor = new PluginDescriptor
        {
            PluginId = "bad", Name = "bad", Version = "1.0.0",
            Status = PluginStatus.Loaded, LoadedAt = DateTime.UtcNow, Manifest = manifest,
        };
        _registry.Register(descriptor);
        _registry.GetRuntime("bad")!.Assembly = typeof(FakePlugin).Assembly;

        var result = await MakeActivator().ActivateAsync("bad", default);

        result.Should().BeFalse();
        _registry.GetRuntime("bad")!.Descriptor.Status.Should().Be(PluginStatus.Failed);
    }

    [Fact]
    public async Task ActivateAsync_TypeNotIPlugin_ReturnsFalse()
    {
        RegisterPlugin("notplugin", typeof(NotAPlugin), typeof(NotAPlugin).Assembly);
        var result = await MakeActivator().ActivateAsync("notplugin", default);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateAsync_NoParameterlessConstructor_ReturnsFalse()
    {
        RegisterPlugin("noctor", typeof(NoCtorPlugin), typeof(NoCtorPlugin).Assembly);
        var result = await MakeActivator().ActivateAsync("noctor", default);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateAsync_UnknownPluginId_ReturnsFalse()
    {
        var result = await MakeActivator().ActivateAsync("nonexistent", default);
        result.Should().BeFalse();
    }
}
