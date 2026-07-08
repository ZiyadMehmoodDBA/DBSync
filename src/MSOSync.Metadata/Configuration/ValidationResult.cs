namespace MSOSync.Metadata.Configuration;

public sealed record ValidationError(string Field, string Message);

public sealed class ValidationResult
{
    public static readonly ValidationResult Ok = new(true, []);

    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationResult(bool isValid, IReadOnlyList<ValidationError> errors)
    {
        IsValid = isValid;
        Errors  = errors;
    }

    public static ValidationResult Fail(IReadOnlyList<ValidationError> errors) => new(false, errors);
}
