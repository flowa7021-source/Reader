using FluentAssertions;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class DocumentCacheManagerTests : IDisposable
{
    private readonly DocumentCacheManager _sut = new();

    public void Dispose() => _sut.Dispose();

    // ───── S3/F ─────

    [Fact]
    public void GetThumbnails_SameFingerprint_ReturnsSameInstance()
    {
        var a = _sut.GetThumbnails("fp-A");
        var b = _sut.GetThumbnails("fp-A");

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void GetTextLayers_SameFingerprint_ReturnsSameInstance()
    {
        var a = _sut.GetTextLayers("fp-A");
        var b = _sut.GetTextLayers("fp-A");

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void GetThumbnails_DifferentFingerprints_ReturnDifferentInstances()
    {
        var a = _sut.GetThumbnails("fp-A");
        var b = _sut.GetThumbnails("fp-B");

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void EvictForDocument_ClearsAndRemovesBothCaches()
    {
        var thumbs = _sut.GetThumbnails("fp-X");
        thumbs.Put(0, new byte[] { 1, 2, 3 });
        var texts = _sut.GetTextLayers("fp-X");

        _sut.EvictForDocument("fp-X");

        // After eviction, next Get returns a fresh (empty) instance.
        _sut.GetThumbnails("fp-X").Should().NotBeSameAs(thumbs);
        _sut.GetThumbnails("fp-X").Count.Should().Be(0);
        thumbs.Count.Should().Be(0); // original was Cleared
    }

    [Fact]
    public void EvictForDocument_UnknownFingerprint_IsNoOp()
    {
        var act = () => _sut.EvictForDocument("never-seen");
        act.Should().NotThrow();
    }

    [Fact]
    public void TrackedDocumentCount_ReflectsGetThumbnailsCalls()
    {
        _sut.GetThumbnails("fp-1");
        _sut.GetThumbnails("fp-2");
        _sut.GetThumbnails("fp-1"); // same as fp-1 — no new entry

        _sut.TrackedDocumentCount.Should().Be(2);
    }

    [Fact]
    public void TrackedDocumentCount_DecreasesAfterEvict()
    {
        _sut.GetThumbnails("fp-A");
        _sut.GetThumbnails("fp-B");

        _sut.EvictForDocument("fp-A");

        _sut.TrackedDocumentCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_AllowsSubsequentCallsToThrow()
    {
        _sut.Dispose(); // first dispose is fine
        var act = () => _sut.GetThumbnails("fp");
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        _sut.Dispose();
        var act = () => _sut.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void GetThumbnails_NullArg_Throws()
    {
        var act = () => _sut.GetThumbnails(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetTextLayers_NullArg_Throws()
    {
        var act = () => _sut.GetTextLayers(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
