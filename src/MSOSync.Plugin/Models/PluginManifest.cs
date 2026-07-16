using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Models;

public sealed record PluginManifest
{
    [JsonPropertyName("manifestVersion")] public int     ManifestVersion { get; init; } = 1;
    [JsonPropertyName("id")]              public string  Id              { get; init; } = null!;
    [JsonPropertyName("name")]            public string  Name            { get; init; } = null!;
    [JsonPropertyName("version")]         public string  Version         { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]      public string? SdkVersion      { get; init; }
    [JsonPropertyName("apiVersion")]      public string? ApiVersion      { get; init; }
    [JsonPropertyName("startupOrder")]    public int     StartupOrder    { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]  public string  MinHostVersion  { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]  public string  MaxHostVersion  { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]   public string  EntryAssembly   { get; init; } = null!;
    [JsonPropertyName("entryType")]       public string  EntryType       { get; init; } = null!;
    [JsonPropertyName("author")]          public string  Author          { get; init; } = null!;
    [JsonPropertyName("description")]     public string  Description     { get; init; } = null!;
    [JsonPropertyName("permissions")]     public IReadOnlyList<string> Permissions  { get; init; } = [];
    [JsonPropertyName("dependencies")]    public IReadOnlyList<string> Dependencies { get; init; } = [];
    [JsonPropertyName("capabilities")]    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
