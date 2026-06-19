// src/MSOSync.Common/Exceptions/DuplicateEntityException.cs
namespace MSOSync.Common.Exceptions;

public sealed class DuplicateEntityException(string message, string code = "DUPLICATE_ENTITY")
    : SyncException(message, code);
