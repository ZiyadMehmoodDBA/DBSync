namespace MSOSync.Transport;

/// <summary>
/// Selects the appropriate ICompressionService for a given target node
/// based on the node's advertised compression capabilities from its most recent heartbeat.
/// </summary>
public interface ICompressionNegotiator
{
    /// <summary>
    /// Returns the best ICompressionService for the given node.
    /// Falls back to gzip if the node has not advertised capabilities
    /// or if the advertised algorithm is unsupported.
    /// </summary>
    ICompressionService SelectFor(string nodeId);
}
