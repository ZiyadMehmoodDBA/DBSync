namespace MSOSync.Api.Dtos.Auth;

public sealed record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
