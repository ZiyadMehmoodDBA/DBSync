namespace MSOSync.Metadata.Audit;

public interface IAuditService
{
    Task WriteAsync(string action, string detail, string actorUsername, CancellationToken ct = default);
}
