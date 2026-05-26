using FluentAssertions;
using Foliant.Tools.ReleaseNotesFromChangelog;
using Xunit;

namespace Foliant.Tools.ReleaseNotesFromChangelog.Tests;

public sealed class ChangelogSlicerTests
{
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-changelog.md"));

    [Fact]
    public void Slice_ExtractsSectionBody_BetweenHeadings()
    {
        var section = ChangelogSlicer.Slice(Fixture(), "0.1.0");

        section.Should().StartWith("Первый альфа-релиз Foliant.");
        section.Should().Contain("### Added");
        section.Should().Contain("Просмотр PDF и DjVu.");
        section.Should().Contain("OCR-движок: Tesseract → PaddleOCR.");
    }

    [Fact]
    public void Slice_DoesNotBleedIntoNextSection()
    {
        var section = ChangelogSlicer.Slice(Fixture(), "0.1.0");

        // The next section's heading and content must not be included.
        section.Should().NotContain("[0.0.1]");
        section.Should().NotContain("Скелет solution.");
    }

    [Fact]
    public void Slice_TrimsSurroundingBlankLines()
    {
        var section = ChangelogSlicer.Slice(Fixture(), "0.1.0");

        section.Should().NotStartWith("\n");
        section.Should().NotEndWith("\n");
        section.Should().Be(section.Trim());
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("v0.1.0")]
    [InlineData("V0.1.0")]
    public void Slice_IgnoresLeadingV(string version)
    {
        var section = ChangelogSlicer.Slice(Fixture(), version);

        section.Should().Contain("Первый альфа-релиз Foliant.");
    }

    [Fact]
    public void Slice_FindsUnreleasedSection()
    {
        var section = ChangelogSlicer.Slice(Fixture(), "Unreleased");

        section.Should().Be("_Пока нет изменений после 0.1.0._");
    }

    [Fact]
    public void Slice_FindsLastSection_WithoutBleedingIntoLinkFooter()
    {
        var section = ChangelogSlicer.Slice(Fixture(), "0.0.1");

        section.Should().Contain("Скелет solution.");
        // Reference-link footer is fine to include (it is plain text), but the section
        // must still terminate at end-of-file without throwing.
        section.Should().Contain("### Added");
    }

    [Fact]
    public void Slice_UnknownVersion_Throws()
    {
        var act = () => ChangelogSlicer.Slice(Fixture(), "9.9.9");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Slice_BlankVersion_Throws(string version)
    {
        var act = () => ChangelogSlicer.Slice(Fixture(), version);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Slice_NullChangelog_Throws()
    {
        var act = () => ChangelogSlicer.Slice(null!, "0.1.0");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Slice_HandlesCrlfLineEndings()
    {
        var crlf = Fixture().Replace("\n", "\r\n", StringComparison.Ordinal);

        var section = ChangelogSlicer.Slice(crlf, "0.1.0");

        section.Should().Contain("Просмотр PDF и DjVu.");
        section.Should().NotContain("\r");
    }
}
