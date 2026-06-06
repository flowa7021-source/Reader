using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigAttachmentService"/>: embed file attachments into
/// the 10-page asset via the catalog <c>/Names → /EmbeddedFiles</c> name-tree, then list / extract /
/// remove them through the same production service and assert fidelity (including binary and Unicode
/// payloads). Pure managed PdfPig — no native runtime — so no Slow trait (mirrors
/// <see cref="PdfPageLabelServiceTests"/>).
/// </summary>
public sealed class PdfAttachmentServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigAttachmentService _service = new(NullLogger<PdfPigAttachmentService>.Instance);

    public PdfAttachmentServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-attachment-" + Guid.NewGuid().ToString("N"));
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
    public async Task Add_ThenList_ShowsAttachment()
    {
        string target = TargetPath();
        string src = WritePayload("notes.txt", "hello attachment"u8.ToArray());

        await _service.AddAsync(Asset, target, src, "my notes", default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        var only = list[0];
        only.Name.Should().Be("notes.txt");
        only.Size.Should().Be("hello attachment"u8.Length);
        only.Description.Should().Be("my notes");
    }

    [Fact]
    public async Task Add_NullDescription_ListsWithNullDescription()
    {
        string target = TargetPath();
        string src = WritePayload("plain.bin", [1, 2, 3, 4, 5]);

        await _service.AddAsync(Asset, target, src, null, default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        list[0].Description.Should().BeNull();
        list[0].Size.Should().Be(5);
    }

    [Fact]
    public async Task Add_ThenExtract_PreservesBytesExactly()
    {
        string target = TargetPath();
        byte[] payload = "round-trip payload éÿ"u8.ToArray();
        string src = WritePayload("data.txt", payload);
        string before = Sha256Bytes(payload);

        await _service.AddAsync(Asset, target, src, null, default);

        string extracted = Path.Combine(_tmpDir, "extracted-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, "data.txt", extracted, default);

        Sha256(extracted).Should().Be(before, "extracted bytes must equal the embedded source bytes");
    }

    [Fact]
    public async Task Add_ThenExtract_BinaryPayloadAllByteValues_RoundTrips()
    {
        string target = TargetPath();
        byte[] payload = new byte[256];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i; // 0x00..0xFF — exercises Latin1 byte-preserving stream body.
        }

        string src = WritePayload("bytes.bin", payload);
        string before = Sha256Bytes(payload);

        await _service.AddAsync(Asset, target, src, null, default);

        string extracted = Path.Combine(_tmpDir, "extracted-bin-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, "bytes.bin", extracted, default);

        Sha256(extracted).Should().Be(before, "all 256 byte values must survive embed + extract");
    }

    [Fact]
    public async Task Add_LargerBinaryPayload_RoundTrips()
    {
        string target = TargetPath();
        byte[] payload = new byte[5000];
        new Random(1234).NextBytes(payload);
        string src = WritePayload("blob.bin", payload);
        string before = Sha256Bytes(payload);

        await _service.AddAsync(Asset, target, src, null, default);

        string extracted = Path.Combine(_tmpDir, "extracted-blob-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, "blob.bin", extracted, default);

        Sha256(extracted).Should().Be(before);
        (await _service.ListAsync(target, default))[0].Size.Should().Be(payload.Length);
    }

    [Fact]
    public async Task Add_TwoDifferentFiles_BothListedSortedByName()
    {
        string target = TargetPath();
        string b = WritePayload("bravo.txt", "B"u8.ToArray());
        string a = WritePayload("alpha.txt", "A"u8.ToArray());

        await _service.AddAsync(Asset, target, b, null, default);
        await _service.AddAsync(target, target, a, null, default); // source == target on second add.

        var list = await _service.ListAsync(target, default);
        list.Select(x => x.Name).Should().Equal("alpha.txt", "bravo.txt");
    }

    [Fact]
    public async Task Add_SameNameTwice_ReplacesWithLatestBytes()
    {
        string target = TargetPath();
        string first = WritePayload("dup.txt", "first version"u8.ToArray());
        await _service.AddAsync(Asset, target, first, "v1", default);

        byte[] second = "second version is longer"u8.ToArray();
        string secondSrc = WritePayload("dup.txt", second);
        await _service.AddAsync(target, target, secondSrc, "v2", default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle("re-adding the same name replaces the entry");
        list[0].Description.Should().Be("v2");
        list[0].Size.Should().Be(second.Length);

        string extracted = Path.Combine(_tmpDir, "dup-extracted-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, "dup.txt", extracted, default);
        Sha256(extracted).Should().Be(Sha256Bytes(second));
    }

    [Fact]
    public async Task Add_PreservesOtherAttachmentsBytes()
    {
        string target = TargetPath();
        byte[] keepBytes = "keep me intact"u8.ToArray();
        string keep = WritePayload("keep.txt", keepBytes);
        string other = WritePayload("other.txt", "other"u8.ToArray());

        await _service.AddAsync(Asset, target, keep, null, default);
        await _service.AddAsync(target, target, other, null, default);

        string extracted = Path.Combine(_tmpDir, "keep-extracted-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, "keep.txt", extracted, default);
        Sha256(extracted).Should().Be(Sha256Bytes(keepBytes), "earlier attachment must survive a later add");
    }

    [Fact]
    public async Task AddThenRemove_ListIsEmpty_AndExtractThrows()
    {
        string target = TargetPath();
        string src = WritePayload("gone.txt", "temporary"u8.ToArray());
        await _service.AddAsync(Asset, target, src, null, default);
        (await _service.ListAsync(target, default)).Should().ContainSingle();

        await _service.RemoveAsync(target, target, "gone.txt", default);

        (await _service.ListAsync(target, default)).Should().BeEmpty();
        var act = () => _service.ExtractAsync(
            target, "gone.txt", Path.Combine(_tmpDir, "x"), default);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Remove_OneOfTwo_KeepsTheOther()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, WritePayload("a.txt", "A"u8.ToArray()), null, default);
        await _service.AddAsync(target, target, WritePayload("b.txt", "B"u8.ToArray()), null, default);

        await _service.RemoveAsync(target, target, "a.txt", default);

        var list = await _service.ListAsync(target, default);
        list.Select(x => x.Name).Should().Equal("b.txt");
    }

    [Fact]
    public async Task Remove_MissingName_IsNoOp_AndKeepsValidPdf()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, WritePayload("present.txt", "X"u8.ToArray()), null, default);

        await _service.RemoveAsync(target, target, "absent.txt", default);

        (await _service.ListAsync(target, default)).Select(x => x.Name).Should().Equal("present.txt");
        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Remove_FromPdfWithoutAttachments_KeepsValidEmptyPdf()
    {
        string target = TargetPath();

        await _service.RemoveAsync(Asset, target, "nothing.txt", default);

        (await _service.ListAsync(target, default)).Should().BeEmpty();
        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Add_UnicodeFileName_RoundTripsThroughListAndExtract()
    {
        string target = TargetPath();
        const string unicodeName = "документ.txt"; // документ.txt
        byte[] payload = "unicode content"u8.ToArray();
        string src = WritePayload(unicodeName, payload);

        await _service.AddAsync(Asset, target, src, "Описание", default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        list[0].Name.Should().Be(unicodeName);
        list[0].Description.Should().Be("Описание");

        string extracted = Path.Combine(_tmpDir, "uni-extracted-" + Guid.NewGuid().ToString("N"));
        await _service.ExtractAsync(target, unicodeName, extracted, default);
        Sha256(extracted).Should().Be(Sha256Bytes(payload));
    }

    [Fact]
    public async Task Write_PreservesPageCount()
    {
        string target = TargetPath();

        await _service.AddAsync(Asset, target, WritePayload("p.txt", "p"u8.ToArray()), null, default);

        PageCount(target).Should().Be(PageCount(Asset));
    }

    [Fact]
    public async Task Add_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);

        await _service.AddAsync(Asset, target, WritePayload("s.txt", "s"u8.ToArray()), "d", default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Remove_DoesNotMutateSource()
    {
        string seeded = TargetPath();
        await _service.AddAsync(Asset, seeded, WritePayload("r.txt", "r"u8.ToArray()), null, default);
        string before = Sha256(seeded);

        string target = TargetPath();
        await _service.RemoveAsync(seeded, target, "r.txt", default);

        Sha256(seeded).Should().Be(before, "remove must not touch the source file");
    }

    [Fact]
    public async Task List_RawAsset_IsEmpty()
    {
        (await _service.ListAsync(Asset, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_MissingName_Throws()
    {
        var act = () => _service.ExtractAsync(Asset, "does-not-exist.txt", Path.Combine(_tmpDir, "x"), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_BlankPath_Throws(string blank)
    {
        var act = () => _service.ListAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BlankSource_Throws(string blank)
    {
        var act = () => _service.AddAsync(blank, TargetPath(), WritePayload("a.txt", [1]), null, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BlankTarget_Throws(string blank)
    {
        var act = () => _service.AddAsync(Asset, blank, WritePayload("a.txt", [1]), null, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BlankFileToAttach_Throws(string blank)
    {
        var act = () => _service.AddAsync(Asset, TargetPath(), blank, null, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankSource_Throws(string blank)
    {
        var act = () => _service.RemoveAsync(blank, TargetPath(), "a.txt", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Extract_BlankTarget_Throws(string blank)
    {
        var act = () => _service.ExtractAsync(Asset, "a.txt", blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Extract_BlankName_Throws(string blank)
    {
        var act = () => _service.ExtractAsync(Asset, blank, TargetPath(), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private string TargetPath() => Path.Combine(_tmpDir, "out-" + Guid.NewGuid().ToString("N") + ".pdf");

    private string WritePayload(string fileName, byte[] bytes)
    {
        // Embed under a deterministic file name regardless of the on-disk temp path (the service uses
        // Path.GetFileName of the source path as the attachment name, so the on-disk name must match).
        string dir = Path.Combine(_tmpDir, "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Sha256Bytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
