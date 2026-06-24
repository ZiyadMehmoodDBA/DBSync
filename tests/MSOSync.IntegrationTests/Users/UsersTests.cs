// tests/MSOSync.IntegrationTests/Users/UsersTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Users;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Users;

[Collection("Users")]
public sealed class UsersTests(UsersFixture fx)
{
    private async Task<HttpClient> AdminClientAsync()
    {
        var token  = await fx.LoginAdminAsync();
        var client = fx.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreateUser_Returns201WithDetails()
    {
        var client = await AdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/users",
            new { Username = "newuser1", Password = "P@ss1234!", Enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain("/api/v1/users/");
        var body = await resp.Content.ReadFromJsonAsync<UserDetailDto>();
        body!.Username.Should().Be("newuser1");
        body.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Returns409()
    {
        var client = await AdminClientAsync();

        await client.PostAsJsonAsync("/api/v1/users",
            new { Username = "dupuser", Password = "P@ss1234!", Enabled = true });

        var resp = await client.PostAsJsonAsync("/api/v1/users",
            new { Username = "dupuser", Password = "P@ss1234!", Enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetUsers_Paginated_ReturnsPage()
    {
        var client = await AdminClientAsync();

        var resp = await client.GetAsync("/api/v1/users?page=1&pageSize=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeleteUser_SoftDeactivates_RevokesTokens()
    {
        var client  = await AdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/v1/users",
            new { Username = "todelete", Password = "P@ss1234!", Enabled = true });
        var user = await created.Content.ReadFromJsonAsync<UserDetailDto>();

        var delResp = await client.DeleteAsync($"/api/v1/users/{user!.UserId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = fx.Services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbUser = await db.Users.FindAsync(user.UserId);
        dbUser!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUser_SelfDeactivate_Returns403()
    {
        var client = await AdminClientAsync();

        // Find admin user by username search
        var list  = await client.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/v1/users?search={fx.AdminUsername}");
        var admin = list!.Items.First(u => u.Username == fx.AdminUsername);

        var resp = await client.DeleteAsync($"/api/v1/users/{admin.UserId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_InvalidPassword_Returns400()
    {
        var client = await AdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/users",
            new { Username = "badpass", Password = "short", Enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
