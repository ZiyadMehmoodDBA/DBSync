using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing.Abstractions;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing;

/// <summary>
/// Loads trusted publisher public keys from the JSON file at startup.
/// Expired keys are filtered out. Cached in memory for O(1) lookup.
/// </summary>
public sealed class TrustedPublisherRegistry : ITrustedPublisherRegistry
{
    private readonly Dictionary<string, PluginSigningKey> _keys;
    private readonly ILogger<TrustedPublisherRegistry>    _logger;

    private static readonly EventId ExpiredKeySkipped = new(2001, "PluginSecurity2001");

    /// <summary>Primary constructor: loads from file specified in options.</summary>
    public TrustedPublisherRegistry(
        IOptions<PluginSecurityOptions>   options,
        ILogger<TrustedPublisherRegistry> logger)
        : this(options, logger, LoadFromFile(options.Value, logger)) { }

    /// <summary>Internal/test constructor: accepts a pre-built list of keys.</summary>
    internal TrustedPublisherRegistry(
        IOptions<PluginSecurityOptions>   options,
        ILogger<TrustedPublisherRegistry> logger,
        IEnumerable<PluginSigningKey>     keys)
    {
        _logger = logger;
        _keys   = new Dictionary<string, PluginSigningKey>(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            if (key.ExpiresAt is not null &&
                DateTime.TryParse(key.ExpiresAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var expiry) &&
                expiry < now)
            {
                _logger.Log(LogLevel.Warning, ExpiredKeySkipped,
                    "Trusted publisher key '{KeyId}' (publisher: '{Publisher}') expired at {Expiry} — skipping.",
                    key.KeyId, key.Publisher, key.ExpiresAt);
                continue;
            }
            _keys[key.KeyId] = key;
        }
    }

    public PluginSigningKey? GetPublicKey(string publicKeyId)
        => _keys.TryGetValue(publicKeyId, out var key) ? key : null;

    public IReadOnlyList<PluginSigningKey> GetAll()
        => _keys.Values.ToList().AsReadOnly();

    // ── file loader ───────────────────────────────────────────────────────────

    private static IEnumerable<PluginSigningKey> LoadFromFile(
        PluginSecurityOptions options, ILogger logger)
    {
        var path = Path.IsPathRooted(options.TrustedPublishersPath)
            ? options.TrustedPublishersPath
            : Path.Combine(AppContext.BaseDirectory, options.TrustedPublishersPath);

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "Trusted publishers file '{Path}' not found. Plugin signature verification will use an empty registry.",
                path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc  = JsonSerializer.Deserialize<TrustedPublishersFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.Publishers ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load trusted publishers from '{Path}'.", path);
            return [];
        }
    }

    private sealed class TrustedPublishersFile
    {
        public List<PluginSigningKey> Publishers { get; init; } = [];
    }
}
