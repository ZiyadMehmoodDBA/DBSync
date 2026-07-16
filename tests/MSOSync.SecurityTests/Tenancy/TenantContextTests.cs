using FluentAssertions;
using MSOSync.Common.Tenancy;
using MSOSync.Security.Tenancy;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantContextTests
{
    [Fact]
    public void TenantContext_Properties_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext(
            tenantId:   tenantId,
            tenantSlug: "acme",
            edition:    EditionType.Enterprise,
            userId:     42L,
            roleId:     7L);

        ctx.TenantId.Should().Be(tenantId);
        ctx.TenantSlug.Should().Be("acme");
        ctx.Edition.Should().Be(EditionType.Enterprise);
        ctx.UserId.Should().Be(42L);
        ctx.RoleId.Should().Be(7L);
        ctx.IsPlatformContext.Should().BeFalse();
    }

    [Fact]
    public void TenantContext_NullableUsers_Allowed()
    {
        var ctx = new TenantContext(
            tenantId:   Guid.NewGuid(),
            tenantSlug: "node-ctx",
            edition:    EditionType.Community,
            userId:     null,
            roleId:     null);

        ctx.UserId.Should().BeNull();
        ctx.RoleId.Should().BeNull();
        ctx.IsPlatformContext.Should().BeFalse();
    }

    [Fact]
    public void PlatformTenantContext_HasCorrectDefaults()
    {
        var ctx = PlatformTenantContext.Instance;

        ctx.TenantId.Should().Be(Guid.Empty);
        ctx.TenantSlug.Should().Be("");
        ctx.UserId.Should().BeNull();
        ctx.RoleId.Should().BeNull();
        ctx.IsPlatformContext.Should().BeTrue();
    }

    [Fact]
    public void TenantAccessException_StoresCode()
    {
        var ex = new TenantAccessException(403, "Membership not found");
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Be("Membership not found");
    }
}
