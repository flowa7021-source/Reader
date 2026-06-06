using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>«Print» (File → Print, Ctrl+P): forwarding-контракт VM ↔ <see cref="IPrintService"/>.
/// UI-сервис (WPF PrintDialog + FixedDocument) сам по себе требует WPF runtime — здесь только
/// проверяем, что VM-команда корректно делегирует, выключается без сервиса/документа и не падает
/// на исключениях сервиса.</summary>
public sealed class DocumentTabViewModelPrintTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/doc.pdf",
        int pageCount = 5,
        IPrintService? printService = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(pageCount);
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
            printService: printService);
    }

    // ───── CanPrint gate ─────

    [Fact]
    public void CanPrint_WithoutService_False()
    {
        var vm = CreateVm(printService: null);
        vm.CanPrint.Should().BeFalse();
    }

    [Fact]
    public void CanPrint_WithEmptyDocument_False()
    {
        var vm = CreateVm(pageCount: 0, printService: Substitute.For<IPrintService>());
        vm.CanPrint.Should().BeFalse();
    }

    [Fact]
    public void CanPrint_WithServiceAndPages_True()
    {
        var vm = CreateVm(pageCount: 1, printService: Substitute.For<IPrintService>());
        vm.CanPrint.Should().BeTrue();
    }

    [Fact]
    public void CanPrint_DocumentNeutral_TrueForNonPdf()
    {
        // Document-neutral: команда работает не только для PDF — gate не проверяет расширение,
        // а только наличие сервиса и страниц. Это явный контракт (см. .Print.cs partial).
        var vm = CreateVm(filePath: "/tmp/book.epub", pageCount: 12, printService: Substitute.For<IPrintService>());
        vm.CanPrint.Should().BeTrue();
    }

    // ───── PrintCommand forwarding ─────

    [Fact]
    public async Task PrintCommand_WithService_CallsServiceWithDocumentAndTitle()
    {
        var printer = Substitute.For<IPrintService>();
        printer.PrintAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(true));
        var vm = CreateVm(filePath: "/tmp/MyDoc.pdf", printService: printer);

        await vm.PrintCommand.ExecuteAsync(null);

        // jobTitle = filename без расширения = "MyDoc". Документ — тот же IDocument, что у VM.
        await printer.Received(1).PrintAsync(
            Arg.Any<IDocument>(),
            "MyDoc",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrintCommand_WithoutService_DoesNotExecute()
    {
        var vm = CreateVm(printService: null);

        vm.PrintCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task PrintCommand_WithEmptyDocument_DoesNotExecute()
    {
        var vm = CreateVm(pageCount: 0, printService: Substitute.For<IPrintService>());

        vm.PrintCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task PrintCommand_ServiceThrows_SwallowsExceptionDoesNotCrash()
    {
        var printer = Substitute.For<IPrintService>();
        printer.PrintAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task<bool>>(_ => throw new InvalidOperationException("printer offline"));
        var vm = CreateVm(printService: printer);

        Func<Task> act = async () => await vm.PrintCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PrintCommand_ServiceReturnsTaskException_SwallowsExceptionDoesNotCrash()
    {
        // Разница с предыдущим тестом: исключение бросается асинхронно (через Task.FromException),
        // а не из самого вызова. Проверяем, что и await-путь catches.
        var printer = Substitute.For<IPrintService>();
        printer.PrintAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromException<bool>(new InvalidOperationException("spooler error")));
        var vm = CreateVm(printService: printer);

        Func<Task> act = async () => await vm.PrintCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PrintCommand_OperationCancelled_DoesNotPropagate()
    {
        // OperationCanceledException — отдельный путь (явно ловим), убеждаемся что не вылетает.
        var printer = Substitute.For<IPrintService>();
        printer.PrintAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromException<bool>(new OperationCanceledException()));
        var vm = CreateVm(printService: printer);

        Func<Task> act = async () => await vm.PrintCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PrintCommand_FilenameWithoutExtension_UsedAsJobTitle()
    {
        // Job title = Path.GetFileNameWithoutExtension(filePath). Проверяем для имени с несколькими
        // точками (только последний компонент — расширение).
        var printer = Substitute.For<IPrintService>();
        printer.PrintAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(true));
        var vm = CreateVm(filePath: "/tmp/Report.2024.q4.pdf", printService: printer);

        await vm.PrintCommand.ExecuteAsync(null);

        await printer.Received(1).PrintAsync(
            Arg.Any<IDocument>(),
            "Report.2024.q4",
            Arg.Any<CancellationToken>());
    }
}
