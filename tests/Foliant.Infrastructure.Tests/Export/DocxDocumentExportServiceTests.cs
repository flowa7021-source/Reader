using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Export;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Export;

public sealed class DocxDocumentExportServiceTests
{
    private readonly DocxDocumentExportService _sut = new();
    private readonly IDocument _doc = Substitute.For<IDocument>();

    [Fact]
    public void SupportedFormats_ContainsDocx()
    {
        _sut.SupportedFormats.Should().ContainSingle().Which.Should().Be("docx");
    }

    [Fact]
    public void CanExport_Docx_True()
    {
        _sut.CanExport("docx").Should().BeTrue();
    }

    [Fact]
    public void CanExport_Pdf_False()
    {
        _sut.CanExport("pdf").Should().BeFalse();
    }

    [Fact]
    public void CanExport_CaseInsensitive()
    {
        _sut.CanExport("DOCX").Should().BeTrue();
        _sut.CanExport("Docx").Should().BeTrue();
    }

    [Fact]
    public void CanExport_NullArg_Throws()
    {
        var act = () => _sut.CanExport(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Export_WritesParagraphPerRun_AndTextSurvivesRoundTrip()
    {
        var layers = new[]
        {
            new TextLayer(0, [new TextRun("Hello", 0, 0, 5, 1), new TextRun("Alpha", 0, 1, 5, 1)]),
            new TextLayer(1, [new TextRun("World", 0, 0, 5, 1)]),
        };
        using var dir = new TempDir();
        string path = dir.File("out.docx");

        int result = await _sut.ExportAsync(_doc, layers, path, "docx", null, CancellationToken.None);

        result.Should().Be(2);
        Body body = ReadBody(path);
        body.InnerText.Should().Contain("Hello").And.Contain("Alpha").And.Contain("World");

        // 3 text paragraphs + 1 page-break paragraph between the two pages.
        body.Elements<Paragraph>().Should().HaveCount(4);
    }

    [Fact]
    public async Task Export_InsertsPageBreakBetweenPages()
    {
        var layers = new[]
        {
            new TextLayer(0, [new TextRun("First", 0, 0, 5, 1)]),
            new TextLayer(1, [new TextRun("Second", 0, 0, 5, 1)]),
        };
        using var dir = new TempDir();
        string path = dir.File("breaks.docx");

        await _sut.ExportAsync(_doc, layers, path, "docx", null, CancellationToken.None);

        Body body = ReadBody(path);

        // v3 EnumValue<T> compares on .Value (per the v2→v3 migration guide).
        body.Descendants<Break>()
            .Count(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().Be(1);
    }

    [Fact]
    public async Task Export_ReportsProgressPerPage()
    {
        var layers = new[]
        {
            TextLayer.Empty(0),
            TextLayer.Empty(1),
            TextLayer.Empty(2),
        };
        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));
        using var dir = new TempDir();
        string path = dir.File("progress.docx");

        int result = await _sut.ExportAsync(_doc, layers, path, "docx", progress, CancellationToken.None);
        await Task.Yield();  // let Progress<T> callbacks fire on thread-pool

        result.Should().Be(3);
        progressValues.Should().HaveCount(3);
        progressValues.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Export_UnsupportedFormat_Throws()
    {
        using var dir = new TempDir();

        var act = async () =>
            await _sut.ExportAsync(_doc, [], dir.File("x.pdf"), "pdf", null, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Export_PreCancelledToken_ThrowsAndDoesNotCreateTargetFile()
    {
        var layers = Enumerable.Range(0, 5).Select(TextLayer.Empty).ToArray();
        using var dir = new TempDir();
        string path = dir.File("cancelled.docx");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
            await _sut.ExportAsync(_doc, layers, path, "docx", null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Export_NullArgs_Throw()
    {
        var layers = Array.Empty<TextLayer>();
        using var dir = new TempDir();
        string path = dir.File("nullargs.docx");

        await ((Func<Task>)(() => _sut.ExportAsync(null!, layers, path, "docx", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, null!, path, "docx", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, layers, null!, "docx", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, layers, path, null!, null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private static Body ReadBody(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return doc.MainDocumentPart!.Document.Body!;
    }
}
