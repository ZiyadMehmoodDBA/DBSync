using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageFileEntry
{
    [JsonPropertyName("path")]   public string Path   { get; init; } = null!;
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = null!;
}
