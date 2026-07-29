using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private readonly Mock<IApiKeyService> _apiKeyService = new();

    private ApiKeyAuthenticationHandler BuildHandler(HttpContext ctx)
    {
        var opts = Options.Create(new AuthenticationSchemeOptions());
        var monitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        monitor.Setup(m => m.Get(It.IsAny<string>())).Returns(opts.Value);

        var handler = new ApiKeyAuthenticationHandler(
            monitor.Object,
            new LoggerFactory(),
            UrlEncoder.Default,
            _apiKeyService.Object);

        var scheme = new AuthenticationScheme("ApiKey", "ApiKey", typeof(ApiKeyAuthenticationHandler));
        handler.InitializeAsync(scheme, ctx).Wait();
        return handler;
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsSuccess_ForValidUserKey()
    {
        var user = new SyncUser { UserId = 1, Username = "alice", PasswordHash = "x" };
        _apiKeyService.Setup(s => s.ValidateUserKeyAsync("msk_testkey12_secretsecretssecret32", default))
            .ReturnsAsync(user);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "msk_testkey12_secretsecretssecret32";

        var handler = BuildHandler(ctx);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNoResult_WhenNoKeyPresent()
    {
        var ctx = new DefaultHttpContext();
        var handler = BuildHandler(ctx);

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsFail_ForInvalidKey()
    {
        _apiKeyService.Setup(s => s.ValidateUserKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync((SyncUser?)null);
        _apiKeyService.Setup(s => s.ValidateServiceAccountKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync((SyncServiceAccount?)null);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "msk_badkey123_badsecretbadsecretbadse";

        var handler = BuildHandler(ctx);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }
}
