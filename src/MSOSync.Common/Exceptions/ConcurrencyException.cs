namespace MSOSync.Common.Exceptions;

public sealed class ConcurrencyException(string message, string code = "CONCURRENCY_CONFLICT")
    : SyncException(message, code);
