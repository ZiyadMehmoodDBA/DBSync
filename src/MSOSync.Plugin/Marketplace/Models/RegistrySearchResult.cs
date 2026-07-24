using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Paged search result envelope from the remote registry search endpoint.</summary>
public sealed record RegistrySearchResult
{
    [JsonPropertyName("data")]       public IReadOnlyList<RegistryPluginEntry> Data       { get; init; } = [];
    [JsonPropertyName("total")]      public int Total      { get; init; }
    [JsonPropertyName("page")]       public int Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int PageSize   { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
}
