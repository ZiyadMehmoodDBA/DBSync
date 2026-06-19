namespace MSOSync.Common.Exceptions;

public sealed class UnauthorizedException(string message, string code = "UNAUTHORIZED")
    : SyncException(message, code);
