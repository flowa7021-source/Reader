using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelXmpTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(string filePath = "/tmp/x.pdf", IPdfXmpService? xmp = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        return new DocumentTabViewModel(document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance, xmpService: xmp);
    }

    [Fact]
    public void CanEditXmp_NoService_False() => CreateVm(xmp: null).CanEditXmp.Should().BeFalse();

    [Fact]
    public void CanEditXmp_NonPdf_False() =>
        CreateVm(filePath: "/tmp/f.epub", xmp: Substitute.For<IPdfXmpService>()).CanEditXmp.Should().BeFalse();

    [Fact]
    public void CanEditXmp_PdfAndService_True() =>
        CreateVm(filePath: "/tmp/f.PDF", xmp: Substitute.For<IPdfXmpService>()).CanEditXmp.Should().BeTrue();

    [Fact]
    public async Task LoadXmpCommand_PopulatesCurrentXmp()
    {
        var svc = Substitute.For<IPdfXmpService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("<xmp/>"));
        var vm = CreateVm(filePath: "/tmp/in.pdf", xmp: svc);

        await vm.LoadXmpCommand.ExecuteAsync(null);

        vm.CurrentXmp.Should().Be("<xmp/>");
    }

    [Fact]
    public async Task LoadXmpCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfXmpService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<string?>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", xmp: svc);

        Func<Task> act = async () => await vm.LoadXmpCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveXmpCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfXmpService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", xmp: svc);

        await vm.SaveXmpCommand.ExecuteAsync(new SaveXmpRequest("<xmp/>", "/tmp/out.pdf"));

        await svc.Received(1).WriteAsync("/tmp/in.pdf", "/tmp/out.pdf", "<xmp/>", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SaveXmpCommand_NonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", xmp: Substitute.For<IPdfXmpService>())
            .SaveXmpCommand.CanExecute(new SaveXmpRequest("x", "/o.pdf")).Should().BeFalse();

    [Fact]
    public async Task SaveXmpCommand_BlankTarget_NoOp()
    {
        var svc = Substitute.For<IPdfXmpService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", xmp: svc);

        await vm.SaveXmpCommand.ExecuteAsync(new SaveXmpRequest("x", "  "));

        await svc.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveXmpCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfXmpService>();
        svc.WriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", xmp: svc);

        Func<Task> act = async () => await vm.SaveXmpCommand.ExecuteAsync(new SaveXmpRequest("x", "/o.pdf"));

        await act.Should().NotThrowAsync();
    }
}
