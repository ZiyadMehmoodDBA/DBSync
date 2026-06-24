namespace MSOSync.Metadata.BatchErrors;

public interface IErrorSeverityClassifier
{
    ErrorSeverity Classify(string? conflictType);

    /// <summary>
    /// Returns non-null conflict type strings that map to the given severity.
    /// Used to translate severity filters into SQL WHERE conflict_type IN (...).
    /// </summary>
    IReadOnlyList<string> GetConflictTypes(ErrorSeverity severity);
}
