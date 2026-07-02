namespace MSOSync.Metadata.Export;

public interface IExportAuditService
{
    Task WriteAsync(string resource, string format, int rowCount, long durationMs, CancellationToken ct = default);
}
