using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Security;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class SecurityAuditControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IAuditChainService> _chain = new();
    private readonly SecurityAuditController _controller;

    public SecurityAuditControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new SecurityAuditController(_db, _chain.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAudit_ReturnsPaginatedEntries()
    {
        for (var i = 0; i < 5; i++)
            _db.Audits.Add(new SyncAudit { ActionName = $"action-{i}", Username = "user1", CreateTime = DateTime.UtcNow, TenantId = Guid.NewGuid() });
        await _db.SaveChangesAsync();

        var result = await _controller.GetAudit(page: 1, pageSize: 3);

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result.Result!).Value;
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyChain_ReturnsIntegrityResult()
    {
        _chain.Setup(c => c.VerifyChainAsync(default))
            .ReturnsAsync((true, (long?)null));

        var result = await _controller.VerifyChain();

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result.Result!).Value!.ToString();
        body.Should().Contain("True");
    }
}
