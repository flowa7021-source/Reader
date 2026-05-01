using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class PlainTextDocumentExportServiceTests
{
    private readonly PlainTextDocumentExportService _sut = new();
    private readonly IDocument _doc = Substitute.For<IDocument>();

    // ───── S14/A ─────

    [Fact]
    public void SupportedFormats_ContainsTxt()
    {
        _sut.SupportedFormats.Should().ContainSingle().Which.Should().Be("txt");
    }

    [Fact]
    public void CanExport_Txt_True()
    {
        _sut.CanExport("txt").Should().BeTrue();
    }

    [Fact]
    public void CanExport_Pdf_False()
    {
        _sut.CanExport("pdf").Should().BeFalse();
    }

    [Fact]
    public void CanExport_CaseInsensitive()
    {
        _sut.CanExport("TXT").Should().BeTrue();
        _sut.CanExport("Txt").Should().BeTrue();
    }

    [Fact]
    public async Task Export_WritesPageHeaders()
    {
        var layers = new[]
        {
            new TextLayer(0, [new TextRun("Hello", 0, 0, 5, 1)]),
            new TextLayer(1, [new TextRun("World", 0, 0, 5, 1)]),
        };
        string path = Path.GetTempFileName();
        try
        {
            await _sut.ExportAsync(_doc, layers, path, "txt", null, CancellationToken.None);

            string content = await File.ReadAllTextAsync(path);
            content.Should().Contain("=== Page 1 ===");
            content.Should().Contain("=== Page 2 ===");
            content.Should().Contain("Hello");
            content.Should().Contain("World");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_ReturnsPageCount()
    {
        var layers = new[]
        {
            TextLayer.Empty(0),
            TextLayer.Empty(1),
            TextLayer.Empty(2),
        };
        string path = Path.GetTempFileName();
        try
        {
            int result = await _sut.ExportAsync(_doc, layers, path, "txt", null, CancellationToken.None);

            result.Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_ReportsProgressPerPage()
    {
        var layers = new[]
        {
            TextLayer.Empty(0),
            TextLayer.Empty(1),
        };
        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));
        string path = Path.GetTempFileName();
        try
        {
            await _sut.ExportAsync(_doc, layers, path, "txt", progress, CancellationToken.None);
            await Task.Yield();  // let Progress<T> callbacks fire on thread-pool

            progressValues.Should().HaveCount(2);
            progressValues.Should().BeInAscendingOrder();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_UnsupportedFormat_Throws()
    {
        var act = async () =>
            await _sut.ExportAsync(_doc, [], Path.GetTempFileName(), "pdf", null, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Export_NullArgs_Throw()
    {
        var layers = Array.Empty<TextLayer>();
        string path = Path.GetTempFileName();

        await ((Func<Task>)(() => _sut.ExportAsync(null!, layers, path, "txt", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, null!, path, "txt", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, layers, null!, "txt", null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _sut.ExportAsync(_doc, layers, path, null!, null, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();

        File.Delete(path);
    }

    [Fact]
    public void CanExport_NullArg_Throws()
    {
        var act = () => _sut.CanExport(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Export_PreCancelledToken_ThrowsAndDoesNotCreateTargetFile()
    {
        var layers = Enumerable.Range(0, 5).Select(i => TextLayer.Empty(i)).ToArray();
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = async () =>
                await _sut.ExportAsync(_doc, layers, path, "txt", null, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
