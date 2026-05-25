using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelAnnotationTests
{
    private static DocumentTabViewModel CreateVm(IAnnotationService annotations)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);

        return new DocumentTabViewModel(
            doc,
            "/tmp/doc.pdf",
            Substitute.For<ISearchService>(),
            annotations,
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public async Task AddNoteAt_CreatesStickyNoteOnCurrentPage()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddNoteAtCommand.ExecuteAsync(new AnnotationPoint(100, 700));

        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.StickyNote);
        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.PageIndex == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNoteAt_NullLocation_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddNoteAtCommand.ExecuteAsync(null);

        vm.CurrentPageAnnotations.Should().BeEmpty();
        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }
}
