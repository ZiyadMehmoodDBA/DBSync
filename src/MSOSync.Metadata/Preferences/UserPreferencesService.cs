using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Preferences;

public sealed class UserPreferencesService(AppDbContext db, ICurrentUserService currentUser)
    : IUserPreferencesService
{
    private async Task<long> GetUserIdAsync(CancellationToken ct)
    {
        var username = currentUser.GetCurrentUsername();
        var userId   = await db.Users.AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => u.UserId)
            .FirstOrDefaultAsync(ct);
        if (userId == 0)
            throw new NotFoundException($"User '{username}' not found.");
        return userId;
    }

    public async Task<Dictionary<string, JsonElement>> GetAllAsync(CancellationToken ct = default)
    {
        var userId = await GetUserIdAsync(ct);
        return await db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(
                p => p.PreferenceKey,
                p => JsonSerializer.Deserialize<JsonElement>(p.PreferenceValue),
                ct);
    }

    public async Task UpsertAsync(string key, JsonElement value, CancellationToken ct = default)
    {
        var userId   = await GetUserIdAsync(ct);
        var existing = await db.UserPreferences
            .Where(p => p.UserId == userId && p.PreferenceKey == key)
            .FirstOrDefaultAsync(ct);
        var json = JsonSerializer.Serialize(value);
        if (existing is null)
        {
            db.UserPreferences.Add(new SyncUserPreference
            {
                UserId          = userId,
                PreferenceKey   = key,
                PreferenceValue = json,
                UpdatedAt       = DateTime.UtcNow,
            });
        }
        else
        {
            existing.PreferenceValue = json;
            existing.UpdatedAt       = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task BulkUpsertAsync(Dictionary<string, JsonElement> preferences, CancellationToken ct = default)
    {
        var userId      = await GetUserIdAsync(ct);
        var existingMap = await db.UserPreferences
            .Where(p => p.UserId == userId && preferences.Keys.Contains(p.PreferenceKey))
            .ToDictionaryAsync(p => p.PreferenceKey, ct);

        foreach (var (key, value) in preferences)
        {
            var json = JsonSerializer.Serialize(value);
            if (existingMap.TryGetValue(key, out var existing))
            {
                existing.PreferenceValue = json;
                existing.UpdatedAt       = DateTime.UtcNow;
            }
            else
            {
                db.UserPreferences.Add(new SyncUserPreference
                {
                    UserId          = userId,
                    PreferenceKey   = key,
                    PreferenceValue = json,
                    UpdatedAt       = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var userId   = await GetUserIdAsync(ct);
        var existing = await db.UserPreferences
            .Where(p => p.UserId == userId && p.PreferenceKey == key)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            db.UserPreferences.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }
}
