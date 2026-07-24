using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

public sealed record RegistryVersionEntry
{
    [JsonPropertyName("version")]        public string   Version        { get; init; } = null!;
    [JsonPropertyName("minHostVersion")] public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")] public string   MaxHostVersion { get; init; } = null!;
    [JsonPropertyName("publishedAt")]    public DateTime PublishedAt    { get; init; }
    [JsonPropertyName("downloadUrl")]    public string   DownloadUrl    { get; init; } = null!;
    [JsonPropertyName("sha256")]         public string   Sha256         { get; init; } = null!;
    [JsonPropertyName("releaseNotes")]   public string?  ReleaseNotes   { get; init; }
    [JsonPropertyName("deprecated")]     public bool     Deprecated     { get; init; }
}
