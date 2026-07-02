# Task 1: Backend Entity + Service

**Part of:** Epic 11E — User Preferences & Saved Workspaces  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11e-user-preferences-design.md`

## Files

**Create:**
- `src/MSOSync.Persistence/Entities/SyncUserPreference.cs`
- `src/MSOSync.Persistence/Configurations/SyncUserPreferenceConfiguration.cs`
- `src/MSOSync.Metadata/Preferences/IUserPreferencesService.cs`
- `src/MSOSync.Metadata/Preferences/UserPreferencesService.cs`
- `tests/MSOSync.MetadataTests/Preferences/UserPreferencesServiceTests.cs`

**Modify:**
- `src/MSOSync.Persistence/AppDbContext.cs` — add `DbSet<SyncUserPreference> UserPreferences`
- `src/MSOSync.Persistence/Migrations/` — add M017 via `dotnet ef migrations add`
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — register `IUserPreferencesService`

## Interfaces Produced (consumed by Task 2)

```csharp
// IUserPreferencesService
Task<Dictionary<string, JsonElement>> GetAllAsync(CancellationToken ct = default);
Task UpsertAsync(string key, JsonElement value, CancellationToken ct = default);
Task BulkUpsertAsync(Dictionary<string, JsonElement> preferences, CancellationToken ct = default);
Task DeleteAsync(string key, CancellationToken ct = default);
```

---

## Global Constraints (apply to every step)

- C# 13, .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9 — `AsNoTracking()` on reads; `SaveChangesAsync(ct)` on writes
- Unit tests: `TestDbContext.Create()` (SQLite in-memory, NOT EF InMemory provider)
- xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72

---

- [ ] **Step 1: Create SyncUserPreference entity**

```csharp
// src/MSOSync.Persistence/Entities/SyncUserPreference.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncUserPreference
{
    public long     PreferenceId    { get; set; }
    public long     UserId          { get; set; }
    public string   PreferenceKey   { get; set; } = "";
    public string   PreferenceValue { get; set; } = "";
    public DateTime UpdatedAt       { get; set; }

    public SyncUser User { get; set; } = null!;
}
```

- [ ] **Step 2: Create EF configuration**

```csharp
// src/MSOSync.Persistence/Configurations/SyncUserPreferenceConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserPreferenceConfiguration
    : IEntityTypeConfiguration<SyncUserPreference>
{
    public void Configure(EntityTypeBuilder<SyncUserPreference> builder)
    {
        builder.ToTable("sync_user_preference", "msosync");
        builder.HasKey(p => p.PreferenceId);
        builder.Property(p => p.PreferenceId).ValueGeneratedOnAdd();
        builder.Property(p => p.PreferenceKey)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(p => p.PreferenceValue)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetime2(7)")
            .HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(p => new { p.UserId, p.PreferenceKey })
            .IsUnique()
            .HasDatabaseName("IX_sync_user_preference_user_key");
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Add DbSet to AppDbContext**

Open `src/MSOSync.Persistence/AppDbContext.cs`. Find the block of DbSet properties (they follow the pattern `public DbSet<X> Xs => Set<X>();`). Add after the last one:

```csharp
public DbSet<SyncUserPreference> UserPreferences => Set<SyncUserPreference>();
```

- [ ] **Step 4: Generate the M017 migration**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
$env:MSOSYNC_CONNECTION_STRING = "Server=(localdb)\MSSQLLocalDB;Database=MSOSyncDev;Trusted_Connection=True;"
dotnet ef migrations add M017_UserPreferences `
  --project src/MSOSync.Persistence `
  --startup-project src/MSOSync.App `
  --output-dir Migrations `
  2>&1 | Select-Object -Last 5
```

Expected: `Done. To undo this action, use 'ef migrations remove'`

Open the generated migration file. Verify it creates the `sync_user_preference` table with the correct columns and the unique index.

- [ ] **Step 5: Write failing tests**

```csharp
// tests/MSOSync.MetadataTests/Preferences/UserPreferencesServiceTests.cs
using System.Text.Json;
using FluentAssertions;
using Moq;
using MSOSync.Common;
using MSOSync.Metadata.Preferences;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Preferences;

public sealed class UserPreferencesServiceTests : IDisposable
{
    private readonly AppDbContext          _db;
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly UserPreferencesService    _sut;
    private const    string TestUsername = "alice";

    public UserPreferencesServiceTests()
    {
        _db = TestDbContext.Create();
        _currentUser.Setup(x => x.GetCurrentUsername()).Returns(TestUsername);
        _sut = new UserPreferencesService(_db, _currentUser.Object);

        _db.Users.Add(new SyncUser
        {
            Username          = TestUsername,
            PasswordHash      = "hash",
            Enabled           = true,
            PasswordChangedAt = DateTime.UtcNow,
            CreatedTime       = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoPreferencesExist()
    {
        var result = await _sut.GetAllAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_StoresValue_ThenGetAllReturnsIt()
    {
        var value = JsonSerializer.Deserialize<JsonElement>("25");
        await _sut.UpsertAsync("page.events.pageSize", value);

        var result = await _sut.GetAllAsync();
        result.Should().ContainKey("page.events.pageSize");
        result["page.events.pageSize"].GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task Upsert_Overwrites_ExistingValue()
    {
        var v1 = JsonSerializer.Deserialize<JsonElement>("25");
        var v2 = JsonSerializer.Deserialize<JsonElement>("50");
        await _sut.UpsertAsync("page.events.pageSize", v1);
        await _sut.UpsertAsync("page.events.pageSize", v2);

        var result = await _sut.GetAllAsync();
        result["page.events.pageSize"].GetInt32().Should().Be(50);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task BulkUpsert_StoresMultipleValues()
    {
        var prefs = new Dictionary<string, JsonElement>
        {
            ["page.events.pageSize"] = JsonSerializer.Deserialize<JsonElement>("25"),
            ["ui.theme"]             = JsonSerializer.Deserialize<JsonElement>("\"dark\""),
        };
        await _sut.BulkUpsertAsync(prefs);

        var result = await _sut.GetAllAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_RemovesExistingKey()
    {
        var value = JsonSerializer.Deserialize<JsonElement>("\"dark\"");
        await _sut.UpsertAsync("ui.theme", value);

        await _sut.DeleteAsync("ui.theme");

        var result = await _sut.GetAllAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_IsIdempotent_WhenKeyNotFound()
    {
        await _sut.Invoking(s => s.DeleteAsync("nonexistent.key"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAll_OnlyReturnsCurrentUsersPreferences()
    {
        _db.Users.Add(new SyncUser
        {
            Username          = "bob",
            PasswordHash      = "hash",
            Enabled           = true,
            PasswordChangedAt = DateTime.UtcNow,
            CreatedTime       = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var bobUser = await _db.Users.FirstAsync(u => u.Username == "bob");
        _db.UserPreferences.Add(new SyncUserPreference
        {
            UserId          = bobUser.UserId,
            PreferenceKey   = "ui.theme",
            PreferenceValue = "\"light\"",
            UpdatedAt       = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var aliceValue = JsonSerializer.Deserialize<JsonElement>("\"dark\"");
        await _sut.UpsertAsync("ui.theme", aliceValue);

        var result = await _sut.GetAllAsync();
        result.Should().HaveCount(1);
        result["ui.theme"].GetString().Should().Be("dark");
    }
}
```

- [ ] **Step 6: Run tests — verify build fails with "UserPreferencesService not found"**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests -c Debug `
  --filter "FullyQualifiedName~UserPreferencesServiceTests" 2>&1 | Select-Object -Last 5
```

Expected: build error referencing missing types.

- [ ] **Step 7: Create IUserPreferencesService**

```csharp
// src/MSOSync.Metadata/Preferences/IUserPreferencesService.cs
using System.Text.Json;

namespace MSOSync.Metadata.Preferences;

public interface IUserPreferencesService
{
    Task<Dictionary<string, JsonElement>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string key, JsonElement value, CancellationToken ct = default);
    Task BulkUpsertAsync(Dictionary<string, JsonElement> preferences, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

- [ ] **Step 8: Create UserPreferencesService**

```csharp
// src/MSOSync.Metadata/Preferences/UserPreferencesService.cs
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
```

- [ ] **Step 9: Register IUserPreferencesService in MetadataServiceExtensions**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. Inside `AddMetadata`, after the Epic 11D block, add:

```csharp
// Epic 11E — User preferences
services.AddScoped<IUserPreferencesService, UserPreferencesService>();
```

Also add the using at the top if not already present:
```csharp
using MSOSync.Metadata.Preferences;
```

- [ ] **Step 10: Run unit tests — all 7 must pass**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests -c Debug `
  --filter "FullyQualifiedName~UserPreferencesServiceTests" 2>&1 | Select-Object -Last 10
```

Expected: 7 passed, 0 failed.

- [ ] **Step 11: Build clean**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: Build succeeded, 0 warning(s).  
Note: `MSOSync.TransportTests` has pre-existing build failures (Epic 6 debt) — ignore those if present; verify `MSOSync.Metadata`, `MSOSync.Api`, `MSOSync.App` build clean.

- [ ] **Step 12: Commit**

```pwsh
git add `
  src/MSOSync.Persistence/Entities/SyncUserPreference.cs `
  src/MSOSync.Persistence/Configurations/SyncUserPreferenceConfiguration.cs `
  src/MSOSync.Persistence/AppDbContext.cs `
  src/MSOSync.Persistence/Migrations/ `
  src/MSOSync.Metadata/Preferences/IUserPreferencesService.cs `
  src/MSOSync.Metadata/Preferences/UserPreferencesService.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Preferences/UserPreferencesServiceTests.cs

git commit -m "feat(11e): add SyncUserPreference entity, M017 migration, IUserPreferencesService + 7 tests"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
