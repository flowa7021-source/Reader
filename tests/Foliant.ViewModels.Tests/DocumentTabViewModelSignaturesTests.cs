using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelSignaturesTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        ISignatureController? controller = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(1);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));
        document.GetSignatures().Returns(controller);

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
            document, filePath, search, ann, bm, NullLogger<DocumentTabViewModel>.Instance);
    }

    private static ISignatureController ControllerWith(params DocumentSignature[] sigs)
    {
        var c = Substitute.For<ISignatureController>();
        c.Signatures.Returns(sigs);
        return c;
    }

    [Fact]
    public void CanViewSignatures_TrueForPdf()
    {
        var vm = CreateVm(filePath: "/tmp/file.pdf");
        vm.CanViewSignatures.Should().BeTrue();
    }

    [Fact]
    public void CanViewSignatures_FalseForNonPdf()
    {
        var vm = CreateVm(filePath: "/tmp/file.epub");
        vm.CanViewSignatures.Should().BeFalse();
    }

    [Fact]
    public void LoadDocumentSignatures_NoController_ReturnsEmpty()
    {
        var vm = CreateVm(controller: null);

        var result = vm.LoadDocumentSignatures();

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadDocumentSignatures_ControllerWithTwoSignatures_ReturnsBoth()
    {
        var s1 = new DocumentSignature("Alice", DateTimeOffset.UnixEpoch, "Approval", null, SignatureKind.Cms);
        var s2 = new DocumentSignature("Bob", DateTimeOffset.UnixEpoch.AddDays(1), null, "Moscow", SignatureKind.PadesB);
        var vm = CreateVm(controller: ControllerWith(s1, s2));

        var result = vm.LoadDocumentSignatures();

        result.Should().HaveCount(2);
        result[0].SignerName.Should().Be("Alice");
        result[1].SignerName.Should().Be("Bob");
        result[1].Kind.Should().Be(SignatureKind.PadesB);
    }

    [Fact]
    public async Task ValidateSignatureAsync_NoController_ReturnsNull()
    {
        var vm = CreateVm(controller: null);

        var result = await vm.ValidateSignatureAsync(
            new DocumentSignature("x", DateTimeOffset.UnixEpoch, null, null, SignatureKind.Cms),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateSignatureAsync_DelegatesToController()
    {
        var sig = new DocumentSignature("Alice", DateTimeOffset.UnixEpoch, null, null, SignatureKind.Cms);
        var expected = new SignatureValidationResult(false, false, false, "not implemented");
        var controller = Substitute.For<ISignatureController>();
        controller.Signatures.Returns([sig]);
        controller.ValidateAsync(sig, Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(expected));
        var vm = CreateVm(controller: controller);

        var result = await vm.ValidateSignatureAsync(sig, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await controller.Received(1).ValidateAsync(sig, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateSignatureAsync_NullSignature_Throws()
    {
        var vm = CreateVm(controller: ControllerWith());

        var act = async () => await vm.ValidateSignatureAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
