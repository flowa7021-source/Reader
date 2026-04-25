using FluentAssertions;
using Foliant.Infrastructure.Storage;
using Xunit;

namespace Foliant.Infrastructure.Tests.Storage;

public sealed class FileFingerprintTests
{
    private readonly FileFingerprint _sut = new();

    [Fact]
    public async Task SameFile_TwoCalls_SameFingerprint()
    {
        using var tmp = new TempDir();
        var path = tmp.File("a.bin");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        var a = await _sut.ComputeAsync(path, default);
        var b = await _sut.ComputeAsync(path, default);

        a.Should().Be(b);
        a.Should().HaveLength(64); // sha256 hex
    }

    [Fact]
    public async Task DifferentContent_DifferentFingerprint()
    {
        using var tmp = new TempDir();
        var p1 = tmp.File("a.bin");
        var p2 = tmp.File("b.bin");

        await File.WriteAllBytesAsync(p1, [1, 2, 3]);
        await File.WriteAllBytesAsync(p2, [4, 5, 6]);

        var f1 = await _sut.ComputeAsync(p1, default);
        var f2 = await _sut.ComputeAsync(p2, default);

        f1.Should().NotBe(f2);
    }

    [Fact]
    public async Task SameContent_DifferentSize_DifferentFingerprint()
    {
        using var tmp = new TempDir();
        var p1 = tmp.File("short.bin");
        var p2 = tmp.File("padded.bin");

        var head = new byte[100];
        new Random(42).NextBytes(head);
        await File.WriteAllBytesAsync(p1, head);

        var padded = new byte[200];
        Array.Copy(head, padded, 100);
        await File.WriteAllBytesAsync(p2, padded);

        var f1 = await _sut.ComputeAsync(p1, default);
        var f2 = await _sut.ComputeAsync(p2, default);

        f1.Should().NotBe(f2);
    }

    [Fact]
    public async Task ChangingMtime_ChangesFingerprint()
    {
        using var tmp = new TempDir();
        var path = tmp.File("a.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        var before = await _sut.ComputeAsync(path, default);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        var after = await _sut.ComputeAsync(path, default);

        before.Should().NotBe(after);
    }

    [Fact]
    public async Task Missing_Throws()
    {
        var act = () => _sut.ComputeAsync("/no/such/file/here.bin", default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
