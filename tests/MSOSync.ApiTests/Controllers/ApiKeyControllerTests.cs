using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Api.Controllers;
using MSOSync.Persistence.Entities;
using System.Security.Claims;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class ApiKeyControllerTests
{
    private readonly Mock<IApiKeyService> _svc = new();
    private readonly ApiKeyController _controller;

    public ApiKeyControllerTests()
    {
        _controller = new ApiKeyController(_svc.Object);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1")], "Test"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task CreateKey_ReturnsRawKeyOnce()
    {
        var entity = new SyncUserApiKey { Id = 1, UserId = 1, KeyPrefix = "msk_abc12345_", Name = "MyKey" };
        _svc.Setup(s => s.CreateUserKeyAsync(1L, "MyKey", null, default))
            .ReturnsAsync(("msk_abc12345_secretsecret32padpad", entity));

        var result = await _controller.CreateKey(new CreateApiKeyRequest("MyKey", null));

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result.Result!).Value!;
        body.ToString().Should().Contain("msk_");
    }

    [Fact]
    public async Task RevokeKey_ReturnsNoContent()
    {
        _svc.Setup(s => s.RevokeUserKeyAsync(5, default)).Returns(Task.CompletedTask);

        var result = await _controller.RevokeKey(5);

        result.Should().BeOfType<NoContentResult>();
    }
}
