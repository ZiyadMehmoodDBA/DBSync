using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
}
