using System.Text.Json;

namespace MSOSync.Metadata.Preferences;

public interface IUserPreferencesService
{
    Task<Dictionary<string, JsonElement>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string key, JsonElement value, CancellationToken ct = default);
    Task BulkUpsertAsync(Dictionary<string, JsonElement> preferences, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
