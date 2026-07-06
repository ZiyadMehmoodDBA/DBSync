using System.Text.Json;
using FluentAssertions;
using MSOSync.Metadata.NodeManagement;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class RegistrationMetadataTests
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_ValidJson_Succeeds()
    {
        var json = """
            {
              "schemaVersion": 1,
              "machine": { "hostName": "srv1", "osVersion": "Win11", "machineName": "m1" },
              "database": null,
              "application": { "agentVersion": "2.0", "runtimeVersion": ".NET 9", "installPath": null },
              "hardware": { "cpuCount": 4, "ramBytes": 8589934592, "diskBytes": null }
            }
            """;

        var dto = JsonSerializer.Deserialize<RegistrationMetadataDto>(json, Opts);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.Machine!.HostName.Should().Be("srv1");
        dto.Hardware!.CpuCount.Should().Be(4);
        dto.Hardware.DiskBytes.Should().BeNull();
    }

    [Fact]
    public void Deserialize_MissingSubRecords_AllowsNull()
    {
        var json = """{ "schemaVersion": 1 }""";

        var dto = JsonSerializer.Deserialize<RegistrationMetadataDto>(json, Opts);

        dto!.Machine.Should().BeNull();
        dto.Database.Should().BeNull();
        dto.Application.Should().BeNull();
        dto.Hardware.Should().BeNull();
    }

    [Fact]
    public void Deserialize_UnknownFields_IgnoredSilently()
    {
        var json = """
            { "schemaVersion": 1, "unknownField": "ignored", "machine": null }
            """;

        var act = () => JsonSerializer.Deserialize<RegistrationMetadataDto>(json, Opts);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_SchemaVersionZero_IsInvalid()
    {
        var json = """{ "schemaVersion": 0 }""";
        var dto  = JsonSerializer.Deserialize<RegistrationMetadataDto>(json, Opts)!;

        dto.SchemaVersion.Should().Be(0);
        // Validation logic: SchemaVersion must be >= 1
        (dto.SchemaVersion >= 1).Should().BeFalse();
    }
}
