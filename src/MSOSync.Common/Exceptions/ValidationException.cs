// src/MSOSync.Common/Exceptions/ValidationException.cs
namespace MSOSync.Common.Exceptions;

public sealed class ValidationException(string message, string code = "VALIDATION_ERROR")
    : SyncException(message, code);
