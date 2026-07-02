using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
