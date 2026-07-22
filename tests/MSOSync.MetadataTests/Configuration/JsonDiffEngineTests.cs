using System.Text.Json;
using FluentAssertions;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Configuration.Dtos;
using Xunit;

namespace MSOSync.MetadataTests.Configuration;

public sealed class JsonDiffEngineTests
{
    private static IReadOnlyList<DiffEntryDto> Diff(string json1, string json2)
    {
        var doc1 = JsonDocument.Parse(json1);
        var doc2 = JsonDocument.Parse(json2);
        return JsonDiffEngine.Diff(doc1.RootElement, doc2.RootElement);
    }

    [Fact]
    public void Diff_identical_json_returns_all_unchanged()
    {
        var json = """{"host":"server01","port":5432}""";
        var entries = Diff(json, json);
        entries.Should().OnlyContain(e => e.ChangeType == "Unchanged");
    }

    [Fact]
    public void Diff_detects_changed_value()
    {
        var json1 = """{"host":"server01"}""";
        var json2 = """{"host":"server02"}""";
        var entries = Diff(json1, json2);
        entries.Should().ContainSingle(e => e.ChangeType == "Changed" && e.Key == "host"
            && e.OldValue == "server01" && e.NewValue == "server02");
    }

    [Fact]
    public void Diff_detects_added_key()
    {
        var json1 = """{"host":"server01"}""";
        var json2 = """{"host":"server01","port":5432}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.ChangeType == "Added" && e.Key == "port" && e.NewValue == "5432");
    }

    [Fact]
    public void Diff_detects_removed_key()
    {
        var json1 = """{"host":"server01","port":5432}""";
        var json2 = """{"host":"server01"}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.ChangeType == "Removed" && e.Key == "port" && e.OldValue == "5432");
    }

    [Fact]
    public void Diff_flattens_nested_objects_with_dot_notation()
    {
        var json1 = """{"database":{"host":"s1","port":1433}}""";
        var json2 = """{"database":{"host":"s2","port":1433}}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.Key == "database.host" && e.ChangeType == "Changed");
        entries.Should().Contain(e => e.Key == "database.port" && e.ChangeType == "Unchanged");
    }

    [Fact]
    public void Diff_treats_arrays_as_atomic()
    {
        var json1 = """{"tags":["a","b"]}""";
        var json2 = """{"tags":["a","c"]}""";
        var entries = Diff(json1, json2);
        entries.Should().ContainSingle(e => e.Key == "tags" && e.ChangeType == "Changed");
    }

    [Fact]
    public void Diff_sorts_changed_first_then_added_then_removed_then_unchanged()
    {
        var json1 = """{"a":"1","b":"2","c":"3"}""";
        var json2 = """{"a":"X","d":"4","c":"3"}""";
        var entries = Diff(json1, json2);
        var types = entries.Select(e => e.ChangeType).ToList();
        var firstChanged   = types.IndexOf("Changed");
        var firstAdded     = types.IndexOf("Added");
        var firstRemoved   = types.IndexOf("Removed");
        var firstUnchanged = types.IndexOf("Unchanged");
        firstChanged.Should().BeLessThan(firstAdded);
        firstAdded.Should().BeLessThan(firstRemoved);
        firstRemoved.Should().BeLessThan(firstUnchanged);
    }
}
