using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Controllers;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class OidcControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OidcController _controller;

    public OidcControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new OidcController(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetConfigurations_ReturnsAll()
    {
        _db.OidcConfigurations.Add(new OidcConfiguration
        {
            Name = "Azure AD",
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "client-1",
            ClientSecretKey = "Oidc:ClientSecret",
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetConfigurations();

        result.Value.Should().HaveCount(1);
        result.Value!.First().Name.Should().Be("Azure AD");
    }

    [Fact]
    public async Task CreateConfiguration_AddsToDb()
    {
        var dto = new OidcConfigurationDto(
            "Google", "https://accounts.google.com", "google-client", "Oidc:ClientSecret:Google");

        var result = await _controller.CreateConfiguration(dto);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _db.OidcConfigurations.Should().ContainSingle(c => c.Name == "Google");
    }

    [Fact]
    public async Task UpdateConfiguration_ModifiesEntity()
    {
        var config = new OidcConfiguration
        {
            Name = "Old Name",
            Authority = "https://old.example.com",
            ClientId = "old-client",
            ClientSecretKey = "old-key",
        };
        _db.OidcConfigurations.Add(config);
        await _db.SaveChangesAsync();

        var dto = new OidcConfigurationDto("New Name", "https://new.example.com", "new-client", "new-key");
        var result = await _controller.UpdateConfiguration(config.Id, dto);

        result.Should().BeOfType<NoContentResult>();
        var updated = await _db.OidcConfigurations.FindAsync(config.Id);
        updated!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteConfiguration_RemovesFromDb()
    {
        var config = new OidcConfiguration
        {
            Name = "ToDelete",
            Authority = "https://auth.example.com",
            ClientId = "c1",
            ClientSecretKey = "k1",
        };
        _db.OidcConfigurations.Add(config);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteConfiguration(config.Id);

        result.Should().BeOfType<NoContentResult>();
        _db.OidcConfigurations.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteConfiguration_ReturnsNotFound_WhenMissing()
    {
        var result = await _controller.DeleteConfiguration(999);
        result.Should().BeOfType<NotFoundResult>();
    }
}
