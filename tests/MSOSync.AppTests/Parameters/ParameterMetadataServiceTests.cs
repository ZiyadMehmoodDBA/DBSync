using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.AppTests.Parameters;

public sealed class ParameterMetadataServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<ICurrentUserService> _currentUser;
    private readonly Mock<IAuditService> _auditSvc;

    public ParameterMetadataServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());

        _mediator = new Mock<IMediator>();
        _mediator
            .Setup(m => m.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _currentUser = new Mock<ICurrentUserService>();
        _currentUser.Setup(u => u.GetCurrentUsername()).Returns("testuser");

        _auditSvc = new Mock<IAuditService>();
        _auditSvc
            .Setup(a => a.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private ParameterMetadataService CreateService()
        => new(_db, _cache, _mediator.Object, _currentUser.Object, _auditSvc.Object);

    private async Task SeedParameterAsync(string name, string? value, string? category)
    {
        _db.Parameters.Add(new SyncParameter
        {
            ParameterName  = name,
            ParameterValue = value,
            Category       = category
        });
        await _db.SaveChangesAsync();
    }

    // Test 1: GetParametersAsync with no category returns all parameters
    [Fact]
    public async Task GetParametersAsync_NoCategoryFilter_ReturnsAll()
    {
        await SeedParameterAsync("Param1", "Value1", "FeatureFlag");
        await SeedParameterAsync("Param2", "Value2", "Timeout");
        await SeedParameterAsync("Param3", "Value3", null);

        var svc = CreateService();
        var all = await svc.GetParametersAsync(null, CancellationToken.None);

        Assert.Equal(3, all.Count);
    }

    // Test 2: GetParametersAsync with category="FeatureFlag" returns only that category
    [Fact]
    public async Task GetParametersAsync_WithCategoryFilter_ReturnsOnlyMatchingCategory()
    {
        await SeedParameterAsync("Flag1", "true", "FeatureFlag");
        await SeedParameterAsync("Flag2", "false", "FeatureFlag");
        await SeedParameterAsync("Timeout1", "30", "Timeout");

        var svc = CreateService();
        var flags = await svc.GetParametersAsync("FeatureFlag", CancellationToken.None);

        Assert.Equal(2, flags.Count);
        Assert.All(flags, p => Assert.Equal("FeatureFlag", p.Category));
    }

    // Test 3: UpdateParameterAsync emits PARAMETER_UPDATED audit with old + new value
    [Fact]
    public async Task UpdateParameterAsync_EmitsAuditWithOldAndNewValue()
    {
        await SeedParameterAsync("MyParam", "oldValue", "General");

        var svc = CreateService();
        await svc.UpdateParameterAsync("MyParam", "newValue", CancellationToken.None);

        _auditSvc.Verify(a => a.WriteAsync(
            "PARAMETER_UPDATED",
            It.Is<string>(s => s.Contains("oldValue") && s.Contains("newValue")),
            "testuser",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 4: UpdateParameterAsync publishes ParameterChangedEvent with OldValue set
    [Fact]
    public async Task UpdateParameterAsync_PublishesParameterChangedEventWithOldValue()
    {
        await SeedParameterAsync("MyParam", "before", "General");

        var svc = CreateService();
        await svc.UpdateParameterAsync("MyParam", "after", CancellationToken.None);

        _mediator.Verify(m => m.Publish(
            It.Is<ParameterChangedEvent>(e =>
                e.ParameterName == "MyParam" &&
                e.OldValue      == "before"  &&
                e.NewValue      == "after"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }
}
