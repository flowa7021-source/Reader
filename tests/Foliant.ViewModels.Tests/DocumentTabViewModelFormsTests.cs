using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelFormsTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "form-vm-" + Guid.NewGuid().ToString("N"));

    public DocumentTabViewModelFormsTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfFormReader? reader = null,
        IPdfFormFillService? fill = null,
        IFormDataFormatCatalog? catalog = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            pdfFormReader: reader,
            pdfFormFillService: fill,
            formDataFormatCatalog: catalog);
    }

    private static IFormDataFormatCatalog CatalogWith(
        params (string Ext, IFormDataExporter? Exp, IFormDataImporter? Imp)[] formats)
    {
        var exporters = formats.Where(f => f.Exp is not null).Select(f => f.Exp!).ToArray();
        var importers = formats.Where(f => f.Imp is not null).Select(f => f.Imp!).ToArray();
        return new FormDataFormatCatalog(exporters, importers);
    }

    private static IFormDataExporter MakeExporter(string ext, string content)
    {
        var e = Substitute.For<IFormDataExporter>();
        e.FormatName.Returns(ext.ToUpperInvariant());
        e.FileExtension.Returns(ext);
        e.Export(Arg.Any<IReadOnlyDictionary<string, string>>()).Returns(content);
        return e;
    }

    private static IFormDataImporter MakeImporter(string ext, IReadOnlyDictionary<string, string> values)
    {
        var i = Substitute.For<IFormDataImporter>();
        i.FormatName.Returns(ext.ToUpperInvariant());
        i.FileExtension.Returns(ext);
        i.Import(Arg.Any<string>()).Returns(values);
        return i;
    }

    // ───── CanExportFormData / CanImportFormData ─────

    [Fact]
    public void CanExportFormData_NoReader_False()
    {
        CreateVm(reader: null, catalog: CatalogWith()).CanExportFormData.Should().BeFalse();
    }

    [Fact]
    public void CanExportFormData_NoCatalog_False()
    {
        CreateVm(reader: Substitute.For<IPdfFormReader>(), catalog: null).CanExportFormData.Should().BeFalse();
    }

    [Fact]
    public void CanExportFormData_NonPdfSource_False()
    {
        CreateVm(filePath: "/tmp/x.djvu",
                 reader: Substitute.For<IPdfFormReader>(),
                 catalog: CatalogWith()).CanExportFormData.Should().BeFalse();
    }

    [Fact]
    public void CanExportFormData_AllPresentAndPdfSource_True()
    {
        CreateVm(filePath: "/tmp/x.PDF",
                 reader: Substitute.For<IPdfFormReader>(),
                 catalog: CatalogWith()).CanExportFormData.Should().BeTrue();
    }

    [Fact]
    public void CanImportFormData_NoFillService_False()
    {
        CreateVm(fill: null, catalog: CatalogWith()).CanImportFormData.Should().BeFalse();
    }

    [Fact]
    public void CanImportFormData_AllPresent_True()
    {
        CreateVm(filePath: "/tmp/x.pdf",
                 fill: Substitute.For<IPdfFormFillService>(),
                 catalog: CatalogWith()).CanImportFormData.Should().BeTrue();
    }

    // ───── ExportFormData ─────

    [Fact]
    public async Task ExportFormData_ResolvesExporterByExtension_AndWritesFile()
    {
        var reader = Substitute.For<IPdfFormReader>();
        var values = new Dictionary<string, string> { ["Name"] = "Alice" };
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(values));

        var exporter = MakeExporter("json", "{\"Name\":\"Alice\"}");
        var catalog = CatalogWith(("json", exporter, null));

        string target = Path.Combine(_tmpDir, "out.json");
        var vm = CreateVm(filePath: Path.Combine(_tmpDir, "in.pdf"), reader: reader, catalog: catalog);

        await vm.ExportFormDataCommand.ExecuteAsync(target);

        File.ReadAllText(target).Should().Be("{\"Name\":\"Alice\"}");
        exporter.Received(1).Export(Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Name"] == "Alice"));
    }

    [Fact]
    public async Task ExportFormData_BlankTargetPath_IsNoOp()
    {
        var reader = Substitute.For<IPdfFormReader>();
        var vm = CreateVm(reader: reader, catalog: CatalogWith());

        await vm.ExportFormDataCommand.ExecuteAsync("  ");

        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task ExportFormData_UnknownExtension_LogsAndNoOp()
    {
        var reader = Substitute.For<IPdfFormReader>();
        var vm = CreateVm(reader: reader, catalog: CatalogWith()); // no exporters registered

        await vm.ExportFormDataCommand.ExecuteAsync(Path.Combine(_tmpDir, "x.unknown"));

        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task ExportFormData_ReaderThrows_DoesNotPropagate()
    {
        var reader = Substitute.For<IPdfFormReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromException<IReadOnlyDictionary<string, string>>(new IOException("boom")));
        var catalog = CatalogWith(("json", MakeExporter("json", "{}"), null));
        var vm = CreateVm(reader: reader, catalog: catalog);

        Func<Task> act = async () => await vm.ExportFormDataCommand.ExecuteAsync(Path.Combine(_tmpDir, "x.json"));

        await act.Should().NotThrowAsync();
    }

    // ───── ImportFormData ─────

    [Fact]
    public async Task ImportFormData_ResolvesImporter_AndCallsFillService()
    {
        var fill = Substitute.For<IPdfFormFillService>();
        var values = new Dictionary<string, string> { ["Name"] = "Bob" };
        var importer = MakeImporter("json", values);
        var catalog = CatalogWith(("json", null, importer));

        string sourcePdf = Path.Combine(_tmpDir, "in.pdf");
        string targetPdf = Path.Combine(_tmpDir, "out.pdf");
        string sourceData = Path.Combine(_tmpDir, "data.json");
        await File.WriteAllTextAsync(sourceData, "{\"Name\":\"Bob\"}");

        var vm = CreateVm(filePath: sourcePdf, fill: fill, catalog: catalog);

        await vm.ImportFormDataCommand.ExecuteAsync(new ImportFormDataRequest(sourceData, targetPdf));

        await fill.Received(1).ApplyAsync(
            sourcePdf,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Name"] == "Bob"),
            targetPdf,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportFormData_NullRequest_IsNoOp()
    {
        var fill = Substitute.For<IPdfFormFillService>();
        var vm = CreateVm(fill: fill, catalog: CatalogWith());

        await vm.ImportFormDataCommand.ExecuteAsync(null);

        await fill.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ImportFormData_FillServiceThrows_DoesNotPropagate()
    {
        var fill = Substitute.For<IPdfFormFillService>();
        fill.ApplyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var importer = MakeImporter("json", new Dictionary<string, string>());
        var catalog = CatalogWith(("json", null, importer));

        string sourceData = Path.Combine(_tmpDir, "data.json");
        await File.WriteAllTextAsync(sourceData, "{}");
        var vm = CreateVm(fill: fill, catalog: catalog);

        Func<Task> act = async () => await vm.ImportFormDataCommand.ExecuteAsync(
            new ImportFormDataRequest(sourceData, Path.Combine(_tmpDir, "out.pdf")));

        await act.Should().NotThrowAsync();
    }
}
