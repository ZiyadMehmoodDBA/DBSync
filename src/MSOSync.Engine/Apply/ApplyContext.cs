using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

internal sealed record ApplyContext(
    SqlConnection                            Connection,
    SqlTransaction                           Transaction,
    Dictionary<string, TriggerApplyMetadata> Metadata);
