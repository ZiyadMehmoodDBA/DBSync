using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using MSOSync.IntegrationTests.Configuration;
using Xunit;

namespace MSOSync.IntegrationTests.SignalR;

[Collection("Configuration")]
public sealed class SignalRResilienceTests(ConfigurationFixture fx)
{
    [Fact]
    public async Task SignalR_ConnectsSuccessfully_WithValidJwt()
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, fx.AdminUsername, fx.AdminPassword);

        var hub = new HubConnectionBuilder()
            .WithUrl(fx.Server.BaseAddress + "hubs/operations", opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult<string?>(token);
                opts.HttpMessageHandlerFactory = _ => fx.Server.CreateHandler();
            })
            .Build();

        await hub.StartAsync();
        hub.State.Should().Be(HubConnectionState.Connected);
        await hub.StopAsync();
    }

    [Fact]
    public async Task SignalR_NoEvents_WithoutAuth()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(fx.Server.BaseAddress + "hubs/operations", opts =>
            {
                opts.HttpMessageHandlerFactory = _ => fx.Server.CreateHandler();
            })
            .Build();

        // Hub requires ViewerOrAbove — unauthenticated connection should fail or be dropped
        var act = async () => await hub.StartAsync();
        await act.Should().ThrowAsync<Exception>();
    }
}
