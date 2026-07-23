using FluentAssertions;
using MSOSync.Plugin.Packaging;
using MSOSync.Plugin.Packaging.Models;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class ManifestV2ValidatorTests
{
    private static ManifestV2 Valid() => new()
    {
        ManifestVersion      = 2,
        Id                   = "test.plugin",
        Name                 = "Test Plugin",
        Version              = "1.0.0",
        SdkVersion           = "1.0",
        SdkVersionConstraint = ">=1.0.0 <2.0.0",
        ApiVersion           = "1",
        MinHostVersion       = "1.0.0",
        MaxHostVersion       = "99.0.0",
        EntryAssembly        = "Test.dll",
        EntryType            = "Test.Plugin",
        Author               = "Test Author",
        Description          = "A test plugin.",
        Files = [new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 64) }],
    };

    [Fact]
    public void Validate_ValidManifest_ReturnsNull()
        => ManifestV2Validator.Validate(Valid()).Should().BeNull();

    [Fact]
    public void Validate_ManifestVersionNot2_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { ManifestVersion = 1 })
            .Should().Contain("manifestVersion");

    [Theory]
    [InlineData("id",                   "")]
    [InlineData("name",                 "")]
    [InlineData("version",              "")]
    [InlineData("sdkVersion",           "")]
    [InlineData("sdkVersionConstraint", "")]
    [InlineData("apiVersion",           "")]
    [InlineData("minHostVersion",       "")]
    [InlineData("maxHostVersion",       "")]
    [InlineData("entryAssembly",        "")]
    [InlineData("entryType",            "")]
    [InlineData("author",               "")]
    [InlineData("description",          "")]
    public void Validate_MissingRequiredField_ReturnsError(string field, string _)
    {
        var m = field switch
        {
            "id"                   => Valid() with { Id                   = "" },
            "name"                 => Valid() with { Name                 = "" },
            "version"              => Valid() with { Version              = "" },
            "sdkVersion"           => Valid() with { SdkVersion           = "" },
            "sdkVersionConstraint" => Valid() with { SdkVersionConstraint = "" },
            "apiVersion"           => Valid() with { ApiVersion           = "" },
            "minHostVersion"       => Valid() with { MinHostVersion       = "" },
            "maxHostVersion"       => Valid() with { MaxHostVersion       = "" },
            "entryAssembly"        => Valid() with { EntryAssembly        = "" },
            "entryType"            => Valid() with { EntryType            = "" },
            "author"               => Valid() with { Author               = "" },
            "description"          => Valid() with { Description          = "" },
            _                      => throw new ArgumentException(field),
        };
        ManifestV2Validator.Validate(m).Should().NotBeNull().And.Contain(field);
    }

    [Fact]
    public void Validate_MissingId_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Id = "" }).Should().Contain("id");

    [Fact]
    public void Validate_InvalidVersionFormat_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Version = "not-a-version" })
            .Should().Contain("version");

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("sub/dir/Evil.dll")]
    [InlineData("sub\\dir\\Evil.dll")]
    public void Validate_PathTraversalInEntryAssembly_ReturnsError(string badPath)
        => ManifestV2Validator.Validate(Valid() with { EntryAssembly = badPath })
            .Should().Contain("entryAssembly");

    [Theory]
    [InlineData("../evil")]
    [InlineData("sub/dir")]
    public void Validate_PathTraversalInId_ReturnsError(string badId)
        => ManifestV2Validator.Validate(Valid() with { Id = badId })
            .Should().Contain("id");

    [Fact]
    public void Validate_EmptyFiles_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Files = [] })
            .Should().Contain("files");

    [Fact]
    public void Validate_DuplicateFilePaths_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 64) };
        var m = Valid() with { Files = [entry, entry] };
        ManifestV2Validator.Validate(m).Should().Contain("duplicate");
    }

    [Fact]
    public void Validate_InvalidSha256Length_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 63) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("sha256");
    }

    [Fact]
    public void Validate_InvalidSha256NotHex_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('z', 64) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("sha256");
    }

    [Fact]
    public void Validate_TooManyKeywords_ReturnsError()
    {
        var m = Valid() with { Keywords = Enumerable.Range(0, 11).Select(i => $"kw{i}").ToList() };
        ManifestV2Validator.Validate(m).Should().Contain("keywords");
    }

    [Fact]
    public void Validate_PathTraversalInFilesEntry_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "../evil.dll", Sha256 = new string('a', 64) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("files");
    }

    [Fact]
    public void Validate_InvalidVersionRange_InPluginDependencies_ReturnsError()
    {
        var dep = new PluginDependencyEntry { Id = "some.dep", VersionRange = "##invalid" };
        var m = Valid() with { PluginDependencies = [dep] };
        ManifestV2Validator.Validate(m).Should().Contain("versionRange");
    }
}
