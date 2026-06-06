using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelAttachmentsTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfAttachmentService? attachments = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);

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
            attachmentService: attachments);
    }

    private static IReadOnlyList<PdfAttachment> Sample() =>
        [new PdfAttachment("notes.txt", 12, "a note"), new PdfAttachment("data.bin", 4096, null)];

    // ───── CanManageAttachments gate ─────

    [Fact]
    public void CanManageAttachments_NoService_False() =>
        CreateVm(attachments: null).CanManageAttachments.Should().BeFalse();

    [Fact]
    public void CanManageAttachments_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.epub", attachments: Substitute.For<IPdfAttachmentService>())
            .CanManageAttachments.Should().BeFalse();

    [Fact]
    public void CanManageAttachments_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", attachments: Substitute.For<IPdfAttachmentService>())
            .CanManageAttachments.Should().BeTrue();

    // ───── LoadAttachmentsCommand ─────

    [Fact]
    public async Task LoadAttachmentsCommand_PopulatesSnapshot()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.LoadAttachmentsCommand.ExecuteAsync(null);

        vm.CurrentAttachments.Should().Equal(Sample());
    }

    [Fact]
    public async Task LoadAttachmentsCommand_ServiceThrows_LeavesEmpty()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfAttachment>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        Func<Task> act = async () => await vm.LoadAttachmentsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentAttachments.Should().BeEmpty();
    }

    // ───── AddAttachmentCommand ─────

    [Fact]
    public async Task AddAttachmentCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.AddAttachmentCommand.ExecuteAsync(new AddAttachmentRequest("/tmp/file.bin", "/tmp/out.pdf", "desc"));

        await svc.Received(1).AddAsync("/tmp/in.pdf", "/tmp/out.pdf", "/tmp/file.bin", "desc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddAttachmentCommand_NonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", attachments: Substitute.For<IPdfAttachmentService>())
            .AddAttachmentCommand.CanExecute(new AddAttachmentRequest("/f", "/o.pdf", null))
            .Should().BeFalse();

    [Fact]
    public async Task AddAttachmentCommand_BlankPaths_NoOp()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.AddAttachmentCommand.ExecuteAsync(new AddAttachmentRequest("  ", "/o.pdf", null));
        await vm.AddAttachmentCommand.ExecuteAsync(new AddAttachmentRequest("/f", "  ", null));

        await svc.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task AddAttachmentCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        svc.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        Func<Task> act = async () =>
            await vm.AddAttachmentCommand.ExecuteAsync(new AddAttachmentRequest("/f", "/o.pdf", null));

        await act.Should().NotThrowAsync();
    }

    // ───── ExtractAttachmentCommand ─────

    [Fact]
    public async Task ExtractAttachmentCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.ExtractAttachmentCommand.ExecuteAsync(new ExtractAttachmentRequest("notes.txt", "/tmp/notes.txt"));

        await svc.Received(1).ExtractAsync("/tmp/in.pdf", "notes.txt", "/tmp/notes.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAttachmentCommand_BlankArgs_NoOp()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.ExtractAttachmentCommand.ExecuteAsync(new ExtractAttachmentRequest("  ", "/tmp/x"));
        await vm.ExtractAttachmentCommand.ExecuteAsync(new ExtractAttachmentRequest("a", "  "));

        await svc.DidNotReceiveWithAnyArgs().ExtractAsync(default!, default!, default!, default);
    }

    // ───── RemoveAttachmentCommand ─────

    [Fact]
    public async Task RemoveAttachmentCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.RemoveAttachmentCommand.ExecuteAsync(new RemoveAttachmentRequest("notes.txt", "/tmp/out.pdf"));

        await svc.Received(1).RemoveAsync("/tmp/in.pdf", "/tmp/out.pdf", "notes.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAttachmentCommand_BlankArgs_NoOp()
    {
        var svc = Substitute.For<IPdfAttachmentService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", attachments: svc);

        await vm.RemoveAttachmentCommand.ExecuteAsync(new RemoveAttachmentRequest("  ", "/o.pdf"));
        await vm.RemoveAttachmentCommand.ExecuteAsync(new RemoveAttachmentRequest("a", "  "));

        await svc.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!, default!, default);
    }
}
