using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestV2
{
    [JsonPropertyName("manifestVersion")]      public int     ManifestVersion      { get; init; } = 2;
    [JsonPropertyName("id")]                   public string  Id                   { get; init; } = null!;
    [JsonPropertyName("name")]                 public string  Name                 { get; init; } = null!;
    [JsonPropertyName("version")]              public string  Version              { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]           public string  SdkVersion           { get; init; } = null!;
    [JsonPropertyName("sdkVersionConstraint")] public string  SdkVersionConstraint { get; init; } = null!;
    [JsonPropertyName("apiVersion")]           public string  ApiVersion           { get; init; } = null!;
    [JsonPropertyName("startupOrder")]         public int     StartupOrder         { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]       public string  MinHostVersion       { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]       public string  MaxHostVersion       { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]        public string  EntryAssembly        { get; init; } = null!;
    [JsonPropertyName("entryType")]            public string  EntryType            { get; init; } = null!;
    [JsonPropertyName("author")]               public string  Author               { get; init; } = null!;
    [JsonPropertyName("authorEmail")]          public string? AuthorEmail          { get; init; }
    [JsonPropertyName("homepage")]             public string? Homepage             { get; init; }
    [JsonPropertyName("license")]              public string? License              { get; init; }
    [JsonPropertyName("description")]          public string  Description          { get; init; } = null!;
    [JsonPropertyName("keywords")]             public IReadOnlyList<string>               Keywords            { get; init; } = [];
    [JsonPropertyName("capabilities")]         public IReadOnlyList<string>               Capabilities        { get; init; } = [];
    [JsonPropertyName("permissions")]          public IReadOnlyList<string>               Permissions         { get; init; } = [];
    [JsonPropertyName("pluginDependencies")]   public IReadOnlyList<PluginDependencyEntry> PluginDependencies { get; init; } = [];
    [JsonPropertyName("files")]                public IReadOnlyList<PackageFileEntry>     Files               { get; init; } = [];
    [JsonPropertyName("signature")]            public ManifestSignatureBlock?             Signature           { get; init; }
}
