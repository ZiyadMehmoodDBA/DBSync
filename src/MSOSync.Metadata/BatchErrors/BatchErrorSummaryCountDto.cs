namespace MSOSync.Metadata.BatchErrors;

public sealed record BatchErrorSummaryCountDto(int Info, int Warning, int Critical, int Total);
