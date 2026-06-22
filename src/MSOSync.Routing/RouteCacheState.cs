namespace MSOSync.Routing;

public sealed class RouteCacheState
{
    private CancellationTokenSource _cts = new();

    public CancellationToken CurrentToken => _cts.Token;

    public void InvalidateAll()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
