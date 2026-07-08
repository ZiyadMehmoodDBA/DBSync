namespace MSOSync.Persistence.Entities;

public enum ConfigurationState
{
    None,             // no template assigned
    Current,          // assigned version == applied version AND hashes match
    UpdateAvailable,  // assigned version != applied version
    Applying,         // node reported Applying status
    Drifted,          // same version but hash mismatch (local modification)
    Failed,           // node reported ApplyFailed
    Unknown,          // ConfigurationStatusReportedAt older than stale threshold
}
