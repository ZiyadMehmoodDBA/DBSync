// src/MSOSync.Common/Exceptions/SyncException.cs
namespace MSOSync.Common.Exceptions;

public abstract class SyncException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
