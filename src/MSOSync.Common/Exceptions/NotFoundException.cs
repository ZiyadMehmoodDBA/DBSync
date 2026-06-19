// src/MSOSync.Common/Exceptions/NotFoundException.cs
namespace MSOSync.Common.Exceptions;

public sealed class NotFoundException(string message, string code = "NOT_FOUND")
    : SyncException(message, code);
