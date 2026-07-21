namespace MSOSync.Common.Exceptions;

public sealed class OperationStateException(string message)
    : SyncException(message, "OPERATION_STATE_INVALID");
