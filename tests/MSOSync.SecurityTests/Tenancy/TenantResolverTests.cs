using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using MSOSync.Common.Tenancy;
using MSOSync.Security.Tenancy;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantResolverTests
{
    private readonly Mock<ITenantAccessValidator> _validatorMock = new();
    private readonly Mock<INodeTenantLookup>      _nodeLookupMock = new();

    private TenantResolver BuildSut() => new(_validatorMock.Object, _nodeLookupMock.Object);

    private static HttpContext BuildContext(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "Bearer");
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var ctx = new DefaultHttpContext(); // unauthenticated
        var sut = BuildSut();

        var act = () => sut.ResolveAsync(ctx, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 401);
    }

    [Fact]
    public async Task PlatformToken_NoTenantIdClaim_ReturnsPlatformContext()
    {
        var ctx = BuildContext(("userId", "1"), ("sub", "admin"));
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeTrue();
        result.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task UserJwt_ValidMembership_ReturnsTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = BuildContext(("userId", "5"), ("tenantId", tenantId.ToString()));
        _validatorMock
            .Setup(v => v.ValidateAsync(tenantId, 5L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantValidationResult(tenantId, "acme", EditionType.Community, RoleId: 3L));
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeFalse();
        result.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("acme");
        result.UserId.Should().Be(5L);
        result.RoleId.Should().Be(3L);
    }

    [Fact]
    public async Task NodeToken_TenantIdMatch_ReturnsTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = BuildContext(("nodeId", "node-01"), ("tenantId", tenantId.ToString()));
        _nodeLookupMock
            .Setup(n => n.GetNodeTenantIdAsync("node-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)tenantId);
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeFalse();
        result.TenantId.Should().Be(tenantId);
        result.UserId.Should().BeNull();
    }

    [Fact]
    public async Task NodeToken_TenantIdMismatch_Returns403()
    {
        var claimedTenantId = Guid.NewGuid();
        var storedTenantId  = Guid.NewGuid(); // different
        var ctx = BuildContext(("nodeId", "node-01"), ("tenantId", claimedTenantId.ToString()));
        _nodeLookupMock
            .Setup(n => n.GetNodeTenantIdAsync("node-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)storedTenantId);
        var sut = BuildSut();

        var act = () => sut.ResolveAsync(ctx, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }
}
