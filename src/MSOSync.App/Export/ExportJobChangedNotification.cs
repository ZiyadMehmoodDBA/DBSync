using MediatR;

namespace MSOSync.App.Export;

public sealed record ExportJobChangedNotification(
    Guid   JobId,
    string RequestedBy,
    string Status,
    int    ProgressPercent,
    long?  RowCount
) : INotification;
