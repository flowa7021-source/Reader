using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigNamedDestinationService"/>: add named destinations
/// into the 10-page asset via the catalog <c>/Names → /Dests</c> name-tree, then list / remove them
/// through the same production service and assert fidelity (names, 0-based page indices, sort order,
/// page-index clamping, source immutability). A hand-built fixture also proves the reader sees the
/// <b>legacy</b> catalog <c>/Dests</c> dictionary form. Pure managed PdfPig — no native runtime — so no
/// Slow trait (mirrors <see cref="PdfAttachmentServiceTests"/>).
/// </summary>
public sealed class PdfNamedDestinationServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigNamedDestinationService _service = new(NullLogger<PdfPigNamedDestinationService>.Instance);

    public PdfNamedDestinationServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-nameddest-" + Guid.NewGuid().ToString("N"));
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
    public async Task Add_Two_ThenList_ShowsBothSortedWithCorrectPageIndices()
    {
        string target = TargetPath();

        await _service.AddAsync(Asset, target, "intro", 1, default); // page 2 → index 1
        await _service.AddAsync(target, target, "appendix", 7, default); // page 8 → index 7

        var list = await _service.ListAsync(target, default);
        list.Should().HaveCount(2);
        list.Select(x => x.Name).Should().Equal("appendix", "intro"); // ordinal sort: 'a' < 'i'
        Index(list, "intro").Should().Be(1);
        Index(list, "appendix").Should().Be(7);
    }

    [Fact]
    public async Task Add_SameNameTwice_ReplacesTarget()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, "ref", 2, default);
        await _service.AddAsync(target, target, "ref", 5, default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle("re-adding the same name replaces the entry");
        list[0].Name.Should().Be("ref");
        list[0].PageIndex.Should().Be(5);
    }

    [Fact]
    public async Task Remove_OneOfTwo_KeepsTheOther()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, "alpha", 0, default);
        await _service.AddAsync(target, target, "bravo", 3, default);

        await _service.RemoveAsync(target, target, "alpha", default);

        var list = await _service.ListAsync(target, default);
        list.Select(x => x.Name).Should().Equal("bravo");
        list[0].PageIndex.Should().Be(3);
    }

    [Fact]
    public async Task AddThenRemove_ListIsEmpty()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, "temp", 4, default);
        (await _service.ListAsync(target, default)).Should().ContainSingle();

        await _service.RemoveAsync(target, target, "temp", default);

        (await _service.ListAsync(target, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_PreservesEarlierDestination()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, "keep", 6, default);
        await _service.AddAsync(target, target, "other", 2, default);

        var list = await _service.ListAsync(target, default);
        Index(list, "keep").Should().Be(6, "earlier destination must survive a later add");
        Index(list, "other").Should().Be(2);
    }

    [Fact]
    public async Task List_RawAsset_IsEmpty()
    {
        (await _service.ListAsync(Asset, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_PageIndexBeyondLast_ClampsToLastPageIndex()
    {
        string target = TargetPath();

        await _service.AddAsync(Asset, target, "end", 999, default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        list[0].PageIndex.Should().Be(9, "page index is clamped to [0, pageCount-1] (10-page asset)");
    }

    [Fact]
    public async Task Add_NegativePageIndex_ClampsToFirstPageIndex()
    {
        string target = TargetPath();

        await _service.AddAsync(Asset, target, "start", -5, default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        list[0].PageIndex.Should().Be(0);
    }

    [Fact]
    public async Task Add_PreservesPageCount()
    {
        string target = TargetPath();

        await _service.AddAsync(Asset, target, "intro", 1, default);

        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Remove_PreservesPageCount()
    {
        string target = TargetPath();
        await _service.AddAsync(Asset, target, "intro", 1, default);

        await _service.RemoveAsync(target, target, "intro", default);

        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Remove_FromPdfWithoutDestinations_KeepsValidPdf()
    {
        string target = TargetPath();

        await _service.RemoveAsync(Asset, target, "nothing", default);

        (await _service.ListAsync(target, default)).Should().BeEmpty();
        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Add_UnicodeName_RoundTrips()
    {
        string target = TargetPath();
        const string unicodeName = "введение"; // введение (intro)

        await _service.AddAsync(Asset, target, unicodeName, 3, default);

        var list = await _service.ListAsync(target, default);
        list.Should().ContainSingle();
        list[0].Name.Should().Be(unicodeName);
        list[0].PageIndex.Should().Be(3);
    }

    [Fact]
    public async Task Add_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);

        await _service.AddAsync(Asset, target, "intro", 1, default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Remove_DoesNotMutateSource()
    {
        string seeded = TargetPath();
        await _service.AddAsync(Asset, seeded, "intro", 1, default);
        string before = Sha256(seeded);

        string target = TargetPath();
        await _service.RemoveAsync(seeded, target, "intro", default);

        Sha256(seeded).Should().Be(before, "remove must not touch the source file");
    }

    [Fact]
    public async Task List_ReadsLegacyCatalogDestsDictionary()
    {
        // PDFium never emits legacy /Dests, so hand-build a fixture to prove the reader sees it.
        byte[] pdf = LegacyDestsPdfFactory.Create(5, [("chapter1", 0), ("chapter2", 3)]);
        string path = Path.Combine(_tmpDir, "legacy-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, pdf, default);

        var list = await _service.ListAsync(path, default);

        list.Select(x => x.Name).Should().Equal("chapter1", "chapter2");
        Index(list, "chapter1").Should().Be(0);
        Index(list, "chapter2").Should().Be(3);
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
        var act = () => _service.AddAsync(blank, TargetPath(), "intro", 1, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BlankTarget_Throws(string blank)
    {
        var act = () => _service.AddAsync(Asset, blank, "intro", 1, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BlankName_Throws(string blank)
    {
        var act = () => _service.AddAsync(Asset, TargetPath(), blank, 1, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankSource_Throws(string blank)
    {
        var act = () => _service.RemoveAsync(blank, TargetPath(), "intro", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankTarget_Throws(string blank)
    {
        var act = () => _service.RemoveAsync(Asset, blank, "intro", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankName_Throws(string blank)
    {
        var act = () => _service.RemoveAsync(Asset, TargetPath(), blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static int Index(IReadOnlyList<PdfNamedDestination> list, string name) =>
        list.Single(x => string.Equals(x.Name, name, StringComparison.Ordinal)).PageIndex;

    private string TargetPath() => Path.Combine(_tmpDir, "out-" + Guid.NewGuid().ToString("N") + ".pdf");

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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
