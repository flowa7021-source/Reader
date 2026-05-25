using FluentAssertions;
using Foliant.Engines.Pdf.Editing;
using UglyToad.PdfPig;
using Xunit;

namespace Foliant.Engines.Pdf.Tests.Editing;

/// <summary>
/// Integration roundtrip через РЕАЛЬНЫЙ dispatch (PdfPig writer, без PDFium native):
/// открыть многостраничный PDF, применить Delete+Reorder редактором, Save, переоткрыть
/// и сверить число/порядок страниц; Undo восстанавливает. Порядок проверяется по
/// уникальной высоте MediaBox из <see cref="MultiPagePdfFactory"/>.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfDocumentEditorRoundtripTests : IDisposable
{
    private const string Fingerprint = "rt-fp";

    private readonly string _tmpDir;
    private readonly FakeEventStore _store = new();

    public PdfDocumentEditorRoundtripTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-editor-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task DeleteThenReorder_Save_PersistsExpectedOrder()
    {
        string path = WritePdf(MultiPagePdfFactory.Create(4));
        var editor = NewEditor(path);

        // delete page 1 → pages {0,2,3}; reorder to {3,2,0} indices of the *current* doc.
        await editor.ApplyAsync(new DeletePageCommand(1), default);
        await editor.ApplyAsync(new ReorderPagesCommand([2, 1, 0]), default);
        await editor.SaveAsync(null, default);

        editor.IsDirty.Should().BeFalse();
        HeightsOf(path).Should().Equal(
            MultiPagePdfFactory.HeightOfPage(3),
            MultiPagePdfFactory.HeightOfPage(2),
            MultiPagePdfFactory.HeightOfPage(0));
    }

    [Fact]
    public async Task Undo_RestoresPreviousState()
    {
        string path = WritePdf(MultiPagePdfFactory.Create(3));
        var editor = NewEditor(path);

        await editor.ApplyAsync(new DeletePageCommand(0), default);
        await editor.UndoAsync(default);
        await editor.SaveAsync(null, default);

        HeightsOf(path).Should().Equal(
            MultiPagePdfFactory.HeightOfPage(0),
            MultiPagePdfFactory.HeightOfPage(1),
            MultiPagePdfFactory.HeightOfPage(2));
    }

    [Fact]
    public async Task Insert_FromOtherFile_Persists()
    {
        string path = WritePdf(MultiPagePdfFactory.Create(2, baseHeightPt: 800));
        string other = WritePdf(MultiPagePdfFactory.Create(1, baseHeightPt: 500));
        var editor = NewEditor(path);

        await editor.ApplyAsync(new InsertPagesCommand(other, 2), default);
        await editor.SaveAsync(null, default);

        var heights = HeightsOf(path);
        heights.Should().HaveCount(3);
        heights[^1].Should().BeApproximately(MultiPagePdfFactory.HeightOfPage(0, 500), 1.0);
    }

    private PdfDocumentEditor NewEditor(string path) =>
        new(File.ReadAllBytes(path), Fingerprint, _store, path);

    private string WritePdf(byte[] bytes)
    {
        string path = Path.Combine(_tmpDir, $"doc-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static double[] HeightsOf(string path)
    {
        using var doc = PdfDocument.Open(path);
        return doc.GetPages().Select(p => p.Height).ToArray();
    }
}
