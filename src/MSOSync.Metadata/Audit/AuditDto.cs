namespace MSOSync.Metadata.Audit;

public sealed record AuditDto(
    long     AuditId,
    string?  Username,
    string?  ActionName,
    string?  ObjectName,
    string?  CorrelationId,
    DateTime CreateTime);
