namespace MSOSync.Engine;

public enum ApplyFailureCategory
{
    DuplicateKey,
    RowNotFound,
    FKViolation,
    MetadataMissing,
    SerializationError,
    Deadlock,
    Timeout,
    SyntaxError,
    Unknown
}
