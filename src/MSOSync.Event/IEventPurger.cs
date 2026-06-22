namespace MSOSync.Event;

public interface IEventPurger
{
    Task<int> PurgeAsync(CancellationToken ct = default);
}
