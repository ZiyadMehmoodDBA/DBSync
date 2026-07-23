namespace MSOSync.Plugin.Signing.Models;

public sealed record PluginSigningKey
{
    /// <summary>Unique key identifier. Matches manifest.signature.publicKeyId.</summary>
    public string  KeyId        { get; init; } = null!;

    /// <summary>Human-readable publisher name.</summary>
    public string  Publisher    { get; init; } = null!;

    /// <summary>
    /// Base64-standard-encoded DER SubjectPublicKeyInfo of the RSA-2048 public key.
    /// Loaded via RSA.Create().ImportSubjectPublicKeyInfo(...).
    /// </summary>
    public string  PublicKeyB64 { get; init; } = null!;

    /// <summary>ISO-8601 UTC datetime when this key was added to the registry.</summary>
    public string  AddedAt      { get; init; } = null!;

    /// <summary>Optional ISO-8601 UTC expiry. Null = never expires.</summary>
    public string? ExpiresAt    { get; init; }
}
