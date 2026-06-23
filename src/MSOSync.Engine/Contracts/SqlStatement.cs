using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed record SqlStatement(
    string                      CommandText,
    IReadOnlyList<SqlParameter> Parameters);
