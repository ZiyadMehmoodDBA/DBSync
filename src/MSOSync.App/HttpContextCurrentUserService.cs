// src/MSOSync.App/HttpContextCurrentUserService.cs
using Microsoft.AspNetCore.Http;
using MSOSync.Common;

namespace MSOSync.App;

public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string GetCurrentUsername() =>
        accessor.HttpContext?.User?.Identity?.Name ?? "system";
}
