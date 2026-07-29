namespace MSOSync.Api.Auth;

public interface IMfaService
{
    Task<string> EnrollAsync(long userId, CancellationToken ct = default);
    Task ConfirmEnrollmentAsync(long userId, string code, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GenerateBackupCodesAsync(long userId, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(long userId, CancellationToken ct = default);
    Task<bool> VerifyTotpAsync(long userId, string code, CancellationToken ct = default);
    Task<bool> VerifyBackupCodeAsync(long userId, string code, CancellationToken ct = default);
}
