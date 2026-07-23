using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PluginDependencyEntry
{
    [JsonPropertyName("id")]           public string Id           { get; init; } = null!;
    [JsonPropertyName("versionRange")] public string VersionRange { get; init; } = null!;
}
