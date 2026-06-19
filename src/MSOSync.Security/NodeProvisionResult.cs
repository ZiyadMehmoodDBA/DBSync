// src/MSOSync.Security/NodeProvisionResult.cs
namespace MSOSync.Security;

public sealed record NodeProvisionResult(string NodeId, string RawToken);
