using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace MSOSync.TestPlugin;

public sealed class TestPlugin : PluginBase
{
    public static bool    InitializeCalled { get; private set; }
    public static bool    StartCalled      { get; private set; }
    public static bool    StopCalled       { get; private set; }
    public static bool    DisposeCalled    { get; private set; }
    public static string? CapturedTimeout  { get; private set; }

    public static void Reset()
    {
        InitializeCalled = false;
        StartCalled      = false;
        StopCalled       = false;
        DisposeCalled    = false;
        CapturedTimeout  = null;
    }

    public override Task InitializeAsync(IPluginContext ctx, CancellationToken ct)
    {
        InitializeCalled = true;
        CapturedTimeout  = ctx.Configuration.GetValue<string>("timeout");
        return base.InitializeAsync(ctx, ct);
    }

    public override Task StartAsync(CancellationToken ct)
    {
        StartCalled = true;
        return base.StartAsync(ct);
    }

    public override Task StopAsync(CancellationToken ct)
    {
        StopCalled = true;
        return base.StopAsync(ct);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return base.DisposeAsync();
    }
}
