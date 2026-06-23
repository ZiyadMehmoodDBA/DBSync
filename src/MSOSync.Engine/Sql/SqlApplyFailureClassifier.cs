namespace MSOSync.Engine;

public sealed class SqlApplyFailureClassifier : IApplyFailureClassifier
{
    public ApplyFailureCategory Classify(int sqlErrorNumber) => sqlErrorNumber switch
    {
        2627 or 2601                => ApplyFailureCategory.DuplicateKey,
        547                         => ApplyFailureCategory.FKViolation,
        1205                        => ApplyFailureCategory.Deadlock,
        -2                          => ApplyFailureCategory.Timeout,
        102 or 208 or 207 or 4121   => ApplyFailureCategory.SyntaxError,
        _                           => ApplyFailureCategory.Unknown
    };
}
