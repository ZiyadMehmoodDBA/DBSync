using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestSignatureBlock
{
    [JsonPropertyName("algorithm")]   public string Algorithm   { get; init; } = null!;
    [JsonPropertyName("publicKeyId")] public string PublicKeyId { get; init; } = null!;
    [JsonPropertyName("value")]       public string Value       { get; init; } = null!;
}
