using System.Collections.Frozen;

namespace MSOSync.Metadata.BatchErrors;

public sealed class ErrorSeverityClassifier : IErrorSeverityClassifier
{
    private static readonly FrozenDictionary<string, ErrorSeverity> Map =
        new Dictionary<string, ErrorSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["DuplicateKey"]    = ErrorSeverity.Info,
            ["Timeout"]         = ErrorSeverity.Warning,
            ["Deadlock"]        = ErrorSeverity.Warning,
            ["SequenceGap"]     = ErrorSeverity.Warning,
            ["MetadataMissing"] = ErrorSeverity.Critical,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> InfoTypes     = GetTypes(ErrorSeverity.Info);
    private static readonly IReadOnlyList<string> WarningTypes  = GetTypes(ErrorSeverity.Warning);
    private static readonly IReadOnlyList<string> CriticalTypes = GetTypes(ErrorSeverity.Critical);

    public ErrorSeverity Classify(string? conflictType)
    {
        if (conflictType is null) return ErrorSeverity.Critical;
        return Map.TryGetValue(conflictType, out var sev) ? sev : ErrorSeverity.Critical;
    }

    public IReadOnlyList<string> GetConflictTypes(ErrorSeverity severity) => severity switch
    {
        ErrorSeverity.Info     => InfoTypes,
        ErrorSeverity.Warning  => WarningTypes,
        ErrorSeverity.Critical => CriticalTypes,
        _                      => Array.Empty<string>()
    };

    private static IReadOnlyList<string> GetTypes(ErrorSeverity target) =>
        Map.Where(kvp => kvp.Value == target)
           .Select(kvp => kvp.Key)
           .ToArray();
}
