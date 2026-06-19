namespace MSOSync.Api.Dtos.Auth;

public sealed record RefreshResponse(string Token, string RefreshToken, DateTime ExpiresAt);
