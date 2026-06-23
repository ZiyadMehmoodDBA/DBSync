namespace MSOSync.Engine;

public interface IApplyFailureClassifier
{
    ApplyFailureCategory Classify(int sqlErrorNumber);
}
