using FluentAssertions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginManifestValidatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _dll;

    public PluginManifestValidatorTests()
    {
        Directory.CreateDirectory(_dir);
        _dll = Path.Combine(_dir, "Test.dll");
        File.WriteAllBytes(_dll, []);  // empty file — just needs to exist
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static PluginManifest Valid(string dll = "Test.dll") => new()
    {
        Id = "test.plugin", Name = "Test", Version = "1.0.0",
        SdkVersion = "1.0", ApiVersion = "1",
        MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
        EntryAssembly = dll, EntryType = "Test.Plugin",
        Author = "Test", Description = "A test plugin",
    };

    [Fact]
    public void Validate_ValidManifest_ReturnsNull()
    {
        var result = PluginManifestValidator.Validate(Valid(), _dir, new HashSet<string>());
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("minHostVersion")]
    [InlineData("maxHostVersion")]
    [InlineData("entryAssembly")]
    [InlineData("entryType")]
    [InlineData("author")]
    [InlineData("description")]
    public void Validate_MissingRequiredField_ReturnsError(string field)
    {
        var m = field switch
        {
            "id"             => Valid() with { Id = "" },
            "name"           => Valid() with { Name = "" },
            "version"        => Valid() with { Version = "" },
            "minHostVersion" => Valid() with { MinHostVersion = "" },
            "maxHostVersion" => Valid() with { MaxHostVersion = "" },
            "entryAssembly"  => Valid() with { EntryAssembly = "" },
            "entryType"      => Valid() with { EntryType = "" },
            "author"         => Valid() with { Author = "" },
            "description"    => Valid() with { Description = "" },
            _                => throw new ArgumentException(field),
        };

        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().NotBeNull();
    }

    [Fact]
    public void Validate_BadSemver_ReturnsError()
    {
        var m = Valid() with { Version = "not-a-version" };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("version");
    }

    [Theory]
    [InlineData("sub/dir/Test.dll")]
    [InlineData("..\\Test.dll")]
    [InlineData("../Test.dll")]
    public void Validate_PathTraversalInEntryAssembly_ReturnsError(string badPath)
    {
        var m = Valid() with { EntryAssembly = badPath };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("path");
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Validate_PathTraversalInId_ReturnsError(string badId)
    {
        var m = Valid() with { Id = badId };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("id").And.Contain("path");
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Validate_PathTraversalInEntryType_ReturnsError(string badType)
    {
        var m = Valid() with { EntryType = badType };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("entryType").And.Contain("path");
    }

    [Fact]
    public void Validate_DuplicateId_ReturnsError()
    {
        var m = Valid();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.plugin" };
        var result = PluginManifestValidator.Validate(m, _dir, seen);
        result.Should().Contain("Duplicate");
    }

    [Fact]
    public void Validate_MissingDll_ReturnsError()
    {
        var m = Valid() with { EntryAssembly = "Missing.dll" };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("not found");
    }

    [Fact]
    public void Validate_DuplicatePermissions_ReturnsError()
    {
        var m = Valid() with { Permissions = ["READ", "READ"] };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("permissions");
    }

    [Fact]
    public void Validate_DuplicateDependencies_ReturnsError()
    {
        var m = Valid() with { Dependencies = ["plugin.a", "plugin.a"] };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("dependencies");
    }

    [Fact]
    public void Validate_DuplicateCapabilities_ReturnsError()
    {
        var m = Valid() with { Capabilities = ["READ", "READ"] };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("capabilities");
    }

    [Fact]
    public void Validate_NullManifest_ReturnsError()
    {
        var result = PluginManifestValidator.Validate(null, _dir, new HashSet<string>());
        result.Should().NotBeNull();
    }
}
