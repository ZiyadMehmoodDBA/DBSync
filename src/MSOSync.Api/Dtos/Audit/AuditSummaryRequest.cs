namespace MSOSync.Api.Dtos.Audit;

public sealed record AuditSummaryRequest
{
    public DateTime From { get; init; }
    public DateTime To   { get; init; }
}
