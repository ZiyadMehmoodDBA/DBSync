namespace MSOSync.Security.Tenancy;

public sealed class TenantAccessException : Exception
{
    public int StatusCode { get; }

    public TenantAccessException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
