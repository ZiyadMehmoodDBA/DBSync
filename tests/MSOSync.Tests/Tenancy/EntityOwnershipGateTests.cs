using System.Reflection;
using FluentAssertions;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.Tests.Tenancy;

public sealed class EntityOwnershipGateTests
{
    private static readonly Assembly PersistenceAssembly = typeof(SyncNode).Assembly;

    [Fact]
    public void AllEntityClasses_HaveExactlyOneOwnershipMarker()
    {
        var entityTypes = PersistenceAssembly
            .GetTypes()
            .Where(t => t.Namespace == "MSOSync.Persistence.Entities"
                     && t.IsClass
                     && !t.IsAbstract
                     && !t.IsEnum)
            .ToList();

        entityTypes.Should().NotBeEmpty("expected to find entity classes");

        var failures = new List<string>();

        foreach (var type in entityTypes)
        {
            var isTenantScoped  = typeof(ITenantScoped).IsAssignableFrom(type);
            var hasTenantScoped = type.GetCustomAttribute<TenantScopedAttribute>() is not null;
            var isGlobal        = type.GetCustomAttribute<GlobalEntityAttribute>()  is not null;
            var isHybrid        = type.GetCustomAttribute<HybridEntityAttribute>()  is not null;

            var markerCount = new[] { isTenantScoped || hasTenantScoped, isGlobal, isHybrid }
                .Count(x => x);

            if (markerCount == 0)
                failures.Add($"{type.Name}: missing ownership marker (add ITenantScoped, [TenantScoped], [GlobalEntity], or [HybridEntity])");
            else if (markerCount > 1 && !(isTenantScoped && hasTenantScoped))
                failures.Add($"{type.Name}: multiple conflicting ownership markers");
        }

        failures.Should().BeEmpty(
            because: "every entity must declare exactly one ownership category");
    }
}
