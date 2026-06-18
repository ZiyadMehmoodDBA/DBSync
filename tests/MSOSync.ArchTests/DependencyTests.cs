using System.IO;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace MSOSync.ArchTests;

public class DependencyTests
{
    private static readonly string[] InternalNamespaces =
    [
        "MSOSync.Common", "MSOSync.Configuration", "MSOSync.Persistence",
        "MSOSync.Security", "MSOSync.Metadata", "MSOSync.Trigger",
        "MSOSync.Event", "MSOSync.Routing", "MSOSync.Node", "MSOSync.Channel",
        "MSOSync.Batch", "MSOSync.Transport", "MSOSync.Engine", "MSOSync.Scheduler",
        "MSOSync.Metrics", "MSOSync.Topology", "MSOSync.Api"
    ];

    [Fact]
    public void Common_HasNoInternalProjectDependencies()
    {
        var outputDir = Path.GetDirectoryName(typeof(DependencyTests).Assembly.Location)!;
        var path = Path.Combine(outputDir, "MSOSync.Common.dll");
        if (File.Exists(path)) Assembly.LoadFrom(path);

        var others = InternalNamespaces.Where(n => n != "MSOSync.Common").ToArray();

        var result = Types.InNamespace("MSOSync.Common")
            .ShouldNot()
            .HaveDependencyOnAny(others)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "MSOSync.Common must not depend on any other MSOSync project. " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void ApiAndApp_AreNotDependedOnByDomainModules()
    {
        var domainNamespaces = InternalNamespaces
            .Where(n => n != "MSOSync.Api" && n != "MSOSync.App")
            .ToArray();

        // Force-load assemblies — .NET loads lazily; GetAssemblies() without this returns an empty set
        var outputDir = Path.GetDirectoryName(typeof(DependencyTests).Assembly.Location)!;
        foreach (var ns in domainNamespaces)
        {
            var path = Path.Combine(outputDir, ns + ".dll");
            if (File.Exists(path)) Assembly.LoadFrom(path);
        }

        var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => domainNamespaces.Any(ns => a.GetName().Name == ns))
            .ToArray();

        var result = Types.InAssemblies(domainAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny("MSOSync.Api", "MSOSync.App")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain modules must not depend on MSOSync.Api or MSOSync.App. " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
