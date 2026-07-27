using System.Text.Json.Serialization;

namespace MSOSync.Cli.Packaging;

/// <summary>
/// CLI-local copy of the plugin manifest schema. Avoids a reference to MSOSync.Plugin.
/// Must stay in sync with MSOSync.Plugin.Models.PluginManifest JSON field names.
/// </summary>
public sealed record CliPluginManifest
{
    [JsonPropertyName("manifestVersion")] public int    ManifestVersion { get; init; } = 1;
    [JsonPropertyName("id")]              public string Id              { get; init; } = null!;
    [JsonPropertyName("name")]            public string Name            { get; init; } = null!;
    [JsonPropertyName("version")]         public string Version         { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]      public string SdkVersion      { get; init; } = "1.0";
    [JsonPropertyName("apiVersion")]      public string ApiVersion      { get; init; } = "1";
    [JsonPropertyName("startupOrder")]    public int    StartupOrder    { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]  public string MinHostVersion  { get; init; } = "1.0.0";
    [JsonPropertyName("maxHostVersion")]  public string MaxHostVersion  { get; init; } = "999.999.999";
    [JsonPropertyName("entryAssembly")]   public string EntryAssembly   { get; init; } = null!;
    [JsonPropertyName("entryType")]       public string EntryType       { get; init; } = null!;
    [JsonPropertyName("author")]          public string Author          { get; init; } = string.Empty;
    [JsonPropertyName("description")]     public string Description     { get; init; } = string.Empty;
    [JsonPropertyName("permissions")]     public IReadOnlyList<string>  Permissions  { get; init; } = [];
    [JsonPropertyName("dependencies")]    public IReadOnlyList<string>  Dependencies { get; init; } = [];
    [JsonPropertyName("capabilities")]    public IReadOnlyList<string>  Capabilities { get; init; } = [];
}
