namespace MSOSync.Metadata.Export;

public interface IExportService<TFilter>
{
    Task<int> ExportCsvAsync(Stream output, TFilter filter, CancellationToken ct);
    Task<int> ExportJsonAsync(Stream output, TFilter filter, CancellationToken ct);
}
