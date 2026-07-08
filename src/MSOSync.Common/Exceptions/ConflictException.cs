namespace MSOSync.Common.Exceptions;

public sealed class ConflictException(string message, string code = "CONFLICT")
    : SyncException(message, code);
