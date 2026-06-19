namespace MSOSync.Security;

public sealed class PasswordPolicy
{
    public const int MinLength = 8;

    public (bool Valid, string? Error) Validate(string password)
    {
        if (password.Length < MinLength)
            return (false, $"Password must be at least {MinLength} characters");
        if (!password.Any(char.IsUpper))
            return (false, "Password must contain at least one uppercase letter");
        if (!password.Any(char.IsDigit))
            return (false, "Password must contain at least one digit");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return (false, "Password must contain at least one special character");
        return (true, null);
    }
}
