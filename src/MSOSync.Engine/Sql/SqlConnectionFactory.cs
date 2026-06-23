using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MSOSync.Engine;

public sealed class SqlConnectionFactory(IConfiguration config) : ISqlConnectionFactory
{
    private readonly string _connectionString =
        config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default not configured");

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
