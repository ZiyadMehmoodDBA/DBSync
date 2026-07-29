namespace MSOSync.Api.Dtos.Auth;

/// <summary>
/// Returned by POST /api/v1/auth/login when the user has TOTP MFA enabled.
/// The client must call POST /api/v1/auth/mfa/verify (or /mfa/backup-verify) with
/// this token to obtain a full access token.
/// </summary>
public sealed record MfaChallengeResponse(
    bool RequiresMfa,
    string MfaToken);
