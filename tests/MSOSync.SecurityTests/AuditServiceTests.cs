using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Security;
using MSOSync.Security.Events;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class AuditServiceTests
{
    private static AppDbContext MakeDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task Handle_LoginSuccess_WritesAuditRecord()
    {
        await using var db = MakeDbContext();
        var svc = new AuditService(db);

        await svc.Handle(new LoginSuccessEvent("alice", "corr-1"), default);

        var audit = await db.Audits.SingleAsync();
        audit.ActionName.Should().Be("LOGIN_SUCCESS");
        audit.Username.Should().Be("alice");
        audit.CorrelationId.Should().Be("corr-1");
        audit.ObjectName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LoginFailure_WritesAuditRecord()
    {
        await using var db = MakeDbContext();
        var svc = new AuditService(db);

        await svc.Handle(new LoginFailureEvent("bob", "corr-2"), default);

        var audit = await db.Audits.SingleAsync();
        audit.ActionName.Should().Be("LOGIN_FAILURE");
        audit.ObjectName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TokenReuseDetected_IncludesFamilyInObjectName()
    {
        await using var db = MakeDbContext();
        var svc = new AuditService(db);

        await svc.Handle(new TokenReuseDetectedEvent("eve", 99L, "corr-3"), default);

        var audit = await db.Audits.SingleAsync();
        audit.ActionName.Should().Be("TOKEN_REUSE_DETECTED");
        audit.ObjectName.Should().Be("family:99");
    }

    [Fact]
    public async Task Handle_AccountLocked_WritesAuditRecord()
    {
        await using var db = MakeDbContext();
        var svc = new AuditService(db);

        await svc.Handle(new AccountLockedEvent("carol", "corr-4"), default);

        var audit = await db.Audits.SingleAsync();
        audit.ActionName.Should().Be("ACCOUNT_LOCKED");
        audit.Username.Should().Be("carol");
    }
}
