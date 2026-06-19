namespace MSOSync.Api.Dtos.Auth;

public sealed record MeResponse(string Username, List<string> Roles);
