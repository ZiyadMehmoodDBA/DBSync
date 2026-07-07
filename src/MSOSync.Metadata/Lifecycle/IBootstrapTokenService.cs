namespace MSOSync.Metadata.Lifecycle;

public interface IBootstrapTokenService
{
    /// <summary>
    /// Revokes all previously active tokens for the node, issues a fresh one-time token.
    /// Returns the RAW token (only time it ever exists in memory; never logged).
    /// Does NOT SaveChanges — caller commits inside its transaction.
    /// </summary>
    Task<string> IssueAsync(string nodeId, string issuedBy, CancellationToken ct = default);

    /// <summary>
    /// True when a live (unconsumed, unexpired, unrevoked) token matches; marks it consumed.
    /// Does NOT SaveChanges — caller commits inside its transaction.
    /// </summary>
    Task<bool> ValidateAndConsumeAsync(string nodeId, string rawToken, CancellationToken ct = default);

    /// <summary>Revokes every live token for the node (recovery approve, decommission). Does NOT SaveChanges.</summary>
    Task RevokeAllAsync(string nodeId, CancellationToken ct = default);
}
