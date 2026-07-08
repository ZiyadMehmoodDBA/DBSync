// tests/MSOSync.IntegrationTests/Configuration/ConfigurationAuthorizationTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Configuration;

[Collection("Configuration")]
public sealed class ConfigurationAuthorizationTests(ConfigurationFixture fx)
{
    private async Task<HttpClient> JwtClientAsync(string username, string password)
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // === Node token must NOT reach user management endpoints ===

    [Fact]
    public async Task NodeToken_Cannot_Call_TemplateList()
    {
        // Node token headers don't satisfy ViewerOrAbove (no role claim) → 401
        var resp = await fx.NodeClient().GetAsync("/api/v1/configuration/templates");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NodeToken_Cannot_Call_AssignEndpoint()
    {
        var resp = await fx.NodeClient().PostAsJsonAsync(
            $"/api/v1/configuration/nodes/{fx.NodeId}/assign",
            new { templateId = Guid.NewGuid(), version = 1 });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // === OPERATOR without ManageConfigurations → 403 ===

    [Fact]
    public async Task Operator_Without_ManageConfigurations_Gets403_OnTemplates()
    {
        var client = await JwtClientAsync(fx.OperatorUsername, fx.OperatorPassword);
        var resp   = await client.GetAsync("/api/v1/configuration/templates");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_Without_ManageConfigurations_Gets403_OnAssignment()
    {
        var client = await JwtClientAsync(fx.OperatorUsername, fx.OperatorPassword);
        var resp   = await client.PostAsJsonAsync(
            $"/api/v1/configuration/nodes/{fx.NodeId}/assign",
            new { templateId = Guid.NewGuid(), version = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_Without_ManageConfigurations_Gets403_OnDrift()
    {
        var client = await JwtClientAsync(fx.OperatorUsername, fx.OperatorPassword);
        var resp   = await client.GetAsync("/api/v1/configuration/drift");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // === User JWT cannot call node-only /configurations/current ===

    [Fact]
    public async Task AdminJwt_Cannot_Call_ConfigurationsCurrent()
    {
        var client = await JwtClientAsync(fx.AdminUsername, fx.AdminPassword);
        var resp   = await client.GetAsync("/api/v1/configurations/current");
        // NodeTokenAuthMiddleware now intercepts /api/v1/configurations/* and
        // expects X-Node-Id / X-Node-Token headers — missing → 401
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "GET /configurations/current requires node token, not user JWT");
    }

    // === Unauthenticated → 401 ===

    [Fact]
    public async Task Unauthenticated_Gets401_OnTemplates()
    {
        var client = fx.CreateClient();
        var resp   = await client.GetAsync("/api/v1/configuration/templates");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // === Admin with ManageConfigurations → 200 ===

    [Fact]
    public async Task Admin_With_ManageConfigurations_Gets200_OnTemplates()
    {
        var client = await JwtClientAsync(fx.AdminUsername, fx.AdminPassword);
        var resp   = await client.GetAsync("/api/v1/configuration/templates");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
