using MSOSync.Sdk.Abstractions;

namespace MSOSync.Sdk.Hosting;

public abstract class PluginBase : IPlugin
{
    protected IPluginContext Context { get; private set; } = null!;

    public virtual Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public virtual Task     StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public virtual Task     StopAsync(CancellationToken cancellationToken)  => Task.CompletedTask;
    public virtual ValueTask DisposeAsync()                                 => ValueTask.CompletedTask;
}
