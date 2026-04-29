using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Annotations;

public sealed class AnnotationServiceTests
{
    private const string Path = "/docs/sample.pdf";
    private const string Fp = "fp-resolved-123";

    private readonly IAnnotationStore _store = Substitute.For<IAnnotationStore>();
    private readonly IFileFingerprint _fingerprint = Substitute.For<IFileFingerprint>();
    private readonly AnnotationService _sut;

    public AnnotationServiceTests()
    {
        _fingerprint.ComputeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Fp);
        _sut = new AnnotationService(_store, _fingerprint, NullLogger<AnnotationService>.Instance);
    }

    [Fact]
    public async Task List_ResolvesFingerprintAndDelegatesToStore()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        _store.ListAsync(Fp, Arg.Any<CancellationToken>()).Returns(new[] { hl });

        var result = await _sut.ListAsync(Path, default);

        result.Should().ContainSingle();
        await _fingerprint.Received(1).ComputeAsync(Path, Arg.Any<CancellationToken>());
        await _store.Received(1).ListAsync(Fp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_ResolvesFingerprintAndDelegates()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);

        await _sut.AddAsync(Path, hl, default);

        await _store.Received(1).AddAsync(Fp, hl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ResolvesFingerprintAndDelegates()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);

        await _sut.UpdateAsync(Path, hl, default);

        await _store.Received(1).UpdateAsync(Fp, hl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_ResolvesFingerprintAndReturnsStoreResult()
    {
        var id = Guid.NewGuid();
        _store.RemoveAsync(Fp, id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RemoveAsync(Path, id, default);

        result.Should().BeTrue();
        await _store.Received(1).RemoveAsync(Fp, id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_NullAnnotation_Throws()
    {
        var act = () => _sut.AddAsync(Path, null!, default);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Update_NullAnnotation_Throws()
    {
        var act = () => _sut.UpdateAsync(Path, null!, default);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
