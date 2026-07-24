using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Single plugin entry from the remote registry catalog.</summary>
public sealed record RegistryPluginEntry
{
    [JsonPropertyName("id")]             public string   Id             { get; init; } = null!;
    [JsonPropertyName("name")]           public string   Name           { get; init; } = null!;
    [JsonPropertyName("author")]         public string   Author         { get; init; } = null!;
    [JsonPropertyName("description")]    public string   Description    { get; init; } = null!;
    [JsonPropertyName("category")]       public string   Category       { get; init; } = null!;
    [JsonPropertyName("tags")]           public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("latestVersion")]  public string   LatestVersion  { get; init; } = null!;
    [JsonPropertyName("minHostVersion")] public string   MinHostVersion { get; init; } = null!;
    [JsonPropertyName("downloadCount")]  public long     DownloadCount  { get; init; }
    [JsonPropertyName("rating")]         public double   Rating         { get; init; }
    [JsonPropertyName("ratingCount")]    public int      RatingCount    { get; init; }
    [JsonPropertyName("publishedAt")]    public DateTime PublishedAt    { get; init; }
    [JsonPropertyName("updatedAt")]      public DateTime UpdatedAt      { get; init; }
    [JsonPropertyName("iconUrl")]        public string?  IconUrl        { get; init; }
    [JsonPropertyName("projectUrl")]     public string?  ProjectUrl     { get; init; }
    [JsonPropertyName("licenseId")]      public string?  LicenseId      { get; init; }
    [JsonPropertyName("verified")]       public bool     Verified       { get; init; }
    [JsonPropertyName("versions")]       public IReadOnlyList<RegistryVersionEntry> Versions { get; init; } = [];
}
