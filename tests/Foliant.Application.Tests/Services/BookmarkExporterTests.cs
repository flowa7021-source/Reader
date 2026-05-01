using System.Text.Json;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class JsonBookmarkExporterTests
{
    private readonly JsonBookmarkExporter _sut = new();

    [Fact]
    public void FormatName_IsJson()
    {
        _sut.FormatName.Should().Be("JSON");
        _sut.FileExtension.Should().Be("json");
    }

    [Fact]
    public void Export_EmptyList_ReturnsValidJsonArray()
    {
        var json = _sut.Export([]);

        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Export_PreservesAllFields()
    {
        var bm = Bookmark.Create(3, "Chapter 4", DateTimeOffset.UtcNow);

        var json = _sut.Export([bm]);

        json.Should().Contain("Chapter 4");
        json.Should().Contain(bm.Id.ToString());
        json.Should().Contain("3");   // PageIndex
    }

    [Fact]
    public void Export_NullArg_Throws()
    {
        var act = () => _sut.Export(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_PreservesUnicode()
    {
        var bm = Bookmark.Create(0, "Глава Привет — мир", DateTimeOffset.UtcNow);

        var json = _sut.Export([bm]);

        // System.Text.Json by default escapes non-ASCII; just check round-trippable.
        var parsed = JsonDocument.Parse(json);
        parsed.RootElement[0].GetProperty("Label").GetString()
            .Should().Be("Глава Привет — мир");
    }
}

public sealed class MarkdownBookmarkExporterTests
{
    private readonly MarkdownBookmarkExporter _sut = new();

    [Fact]
    public void FormatName_IsMarkdown()
    {
        _sut.FormatName.Should().Be("Markdown");
        _sut.FileExtension.Should().Be("md");
    }

    [Fact]
    public void Export_EmptyList_HasHeaderAndPlaceholder()
    {
        var md = _sut.Export([]);

        md.Should().Contain("# Bookmarks");
        md.Should().Contain("_No bookmarks._");
    }

    [Fact]
    public void Export_SortsByPageIndexAscending()
    {
        var b3 = Bookmark.Create(3, "Three", DateTimeOffset.UtcNow);
        var b1 = Bookmark.Create(1, "One", DateTimeOffset.UtcNow);
        var b7 = Bookmark.Create(7, "Seven", DateTimeOffset.UtcNow);

        var md = _sut.Export([b3, b1, b7]);

        int idxOne = md.IndexOf("Page 2 — One", StringComparison.Ordinal);
        int idxThree = md.IndexOf("Page 4 — Three", StringComparison.Ordinal);
        int idxSeven = md.IndexOf("Page 8 — Seven", StringComparison.Ordinal);

        idxOne.Should().BeGreaterThan(0);
        idxOne.Should().BeLessThan(idxThree);
        idxThree.Should().BeLessThan(idxSeven);
    }

    [Fact]
    public void Export_FormatsPageAsOneBased()
    {
        var bm = Bookmark.Create(0, "Title page", DateTimeOffset.UtcNow);

        var md = _sut.Export([bm]);

        md.Should().Contain("Page 1 — Title page");
    }

    [Fact]
    public void Export_NullArg_Throws()
    {
        var act = () => _sut.Export(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_PreservesCyrillicAndDashes()
    {
        var bm = Bookmark.Create(2, "Глава — Введение", DateTimeOffset.UtcNow);

        var md = _sut.Export([bm]);

        md.Should().Contain("Глава — Введение");
    }
}
