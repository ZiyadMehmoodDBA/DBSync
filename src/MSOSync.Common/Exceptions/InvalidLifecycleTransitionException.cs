namespace MSOSync.Common.Exceptions;

/// <summary>
/// Thrown when a node lifecycle transition is denied by the canonical state machine.
/// Carries STRINGS (not the Persistence enum) because MSOSync.Common must not
/// reference MSOSync.Persistence. The typed properties feed the §7.4 error body.
/// </summary>
public sealed class InvalidLifecycleTransitionException(
    string from,
    string requested,
    IReadOnlyList<string> allowedTargets,
    Guid correlationId)
    : SyncException(
        $"Invalid lifecycle transition {from} -> {requested}. Allowed: {string.Join(", ", allowedTargets)}",
        "INVALID_LIFECYCLE_TRANSITION")
{
    public string From { get; } = from;
    public string Requested { get; } = requested;
    public IReadOnlyList<string> AllowedTargets { get; } = allowedTargets;
    public Guid CorrelationId { get; } = correlationId;
}
