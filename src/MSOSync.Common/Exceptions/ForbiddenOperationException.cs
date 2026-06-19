namespace MSOSync.Common.Exceptions;

public sealed class ForbiddenOperationException(string message, string code = "FORBIDDEN")
    : SyncException(message, code);
