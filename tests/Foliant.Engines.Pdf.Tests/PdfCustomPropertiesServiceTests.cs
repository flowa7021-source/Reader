using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigCustomPropertiesService"/>: write custom (non-
/// standard) <c>/Info</c> key/value pairs into a seeded PDF, then read them back through the same
/// production service and assert fidelity, including that the standard fields stay intact. Pure managed
/// PdfPig — no native runtime — so no Slow trait (mirrors <see cref="PdfPageLabelServiceTests"/>).
///
/// <para>The raw asset <c>pdf-text-en-10p.pdf</c> has <b>no</b> <c>/Info</c> dictionary at all (PDFium
/// emitted none), so most tests run against a seeded copy: <see cref="SeedWithStandardInfoAsync"/> uses
/// the existing <see cref="PdfPigMetadataEditService"/> to materialise an <c>/Info</c> object with
/// Title/Author/Producer. The no-<c>/Info</c> asset is used directly to cover the empty-list read and
/// the <see cref="NotSupportedException"/> write path.</para>
/// </summary>
public sealed class PdfCustomPropertiesServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigCustomPropertiesService _service = new(NullLogger<PdfPigCustomPropertiesService>.Instance);
    private readonly PdfPigMetadataEditService _seeder = new();

    public PdfCustomPropertiesServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-customprops-" + Guid.NewGuid().ToString("N"));
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
    public async Task Set_TwoCustomProps_RoundTripsSorted()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();
        // Deliberately unsorted input: ListAsync must return them sorted by Name (ordinal).
        IReadOnlyList<PdfCustomProperty> props =
        [
            new PdfCustomProperty("Status", "Draft"),
            new PdfCustomProperty("Company", "Acme Corp"),
        ];

        await _service.SetAsync(seeded, target, props, default);

        var read = await _service.ListAsync(target, default);
        read.Should().Equal(
            new PdfCustomProperty("Company", "Acme Corp"),
            new PdfCustomProperty("Status", "Draft"));
    }

    [Fact]
    public async Task Set_PreservesStandardFields()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();

        await _service.SetAsync(seeded, target, [new PdfCustomProperty("Company", "Acme Corp")], default);

        using var doc = PdfPigDocument.Open(target);
        doc.Information.Title.Should().Be("My Title");
        doc.Information.Author.Should().Be("Jane Doe");
        doc.Information.Producer.Should().Be("Foliant Producer");
    }

    [Fact]
    public async Task Set_ReplacesPreviousCustomProps()
    {
        string seeded = await SeedWithStandardInfoAsync();

        string first = TargetPath();
        await _service.SetAsync(seeded, first,
            [new PdfCustomProperty("Company", "Acme Corp"), new PdfCustomProperty("Status", "Draft")], default);
        (await _service.ListAsync(first, default)).Should().HaveCount(2);

        string second = TargetPath();
        await _service.SetAsync(first, second, [new PdfCustomProperty("Reviewer", "Bob")], default);

        var read = await _service.ListAsync(second, default);
        read.Should().Equal(new PdfCustomProperty("Reviewer", "Bob"));
    }

    [Fact]
    public async Task Set_EmptyList_RemovesAllCustomProps_KeepsStandardFields()
    {
        string seeded = await SeedWithStandardInfoAsync();

        string seededWithCustom = TargetPath();
        await _service.SetAsync(seeded, seededWithCustom, [new PdfCustomProperty("Company", "Acme Corp")], default);
        (await _service.ListAsync(seededWithCustom, default)).Should().NotBeEmpty();

        string cleared = TargetPath();
        await _service.SetAsync(seededWithCustom, cleared, [], default);

        (await _service.ListAsync(cleared, default)).Should().BeEmpty();
        using var doc = PdfPigDocument.Open(cleared);
        doc.Information.Title.Should().Be("My Title", "clearing custom props must not touch standard fields");
        doc.NumberOfPages.Should().Be(10);
    }

    [Fact]
    public async Task Set_UnicodeNameAndValue_RoundTripsLossless()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();
        var prop = new PdfCustomProperty("Отдел", "Привет, мир — café");

        await _service.SetAsync(seeded, target, [prop], default);

        var read = await _service.ListAsync(target, default);
        read.Should().Equal(prop);
    }

    [Fact]
    public async Task Set_EmptyValue_RoundTrips()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();
        var prop = new PdfCustomProperty("Notes", string.Empty);

        await _service.SetAsync(seeded, target, [prop], default);

        (await _service.ListAsync(target, default)).Should().Equal(prop);
    }

    [Fact]
    public async Task Set_PreservesPageCount()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();

        await _service.SetAsync(seeded, target, [new PdfCustomProperty("Company", "Acme Corp")], default);

        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Set_DoesNotMutateSource()
    {
        string seeded = await SeedWithStandardInfoAsync();
        string target = TargetPath();
        string before = Sha256(seeded);

        await _service.SetAsync(seeded, target, [new PdfCustomProperty("Company", "Acme Corp")], default);

        Sha256(seeded).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Set_SourceEqualsTarget_IsSafe()
    {
        string seeded = await SeedWithStandardInfoAsync();

        await _service.SetAsync(seeded, seeded, [new PdfCustomProperty("Company", "Acme Corp")], default);

        (await _service.ListAsync(seeded, default)).Should().Equal(new PdfCustomProperty("Company", "Acme Corp"));
        PageCount(seeded).Should().Be(10);
    }

    [Fact]
    public async Task Set_DuplicateName_Throws()
    {
        string seeded = await SeedWithStandardInfoAsync();
        IReadOnlyList<PdfCustomProperty> props =
        [
            new PdfCustomProperty("Company", "Acme Corp"),
            new PdfCustomProperty("Company", "Other"),
        ];

        var act = () => _service.SetAsync(seeded, TargetPath(), props, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_BlankName_Throws(string blankName)
    {
        string seeded = await SeedWithStandardInfoAsync();

        var act = () => _service.SetAsync(seeded, TargetPath(), [new PdfCustomProperty(blankName, "x")], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Set_SourceWithoutInfo_ThrowsNotSupported()
    {
        // The raw asset has no /Info dictionary, so an incremental update cannot add one.
        var act = () => _service.SetAsync(Asset, TargetPath(), [new PdfCustomProperty("Company", "Acme Corp")], default);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_BlankSource_Throws(string blank)
    {
        var act = () => _service.SetAsync(blank, TargetPath(), [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_BlankTarget_Throws(string blank)
    {
        var act = () => _service.SetAsync(Asset, blank, [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Set_NullList_Throws()
    {
        var act = () => _service.SetAsync(Asset, TargetPath(), null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task List_RawAssetWithOnlyStandardKeys_ReturnsEmpty()
    {
        // Asset has no custom /Info keys (in fact no /Info at all) → empty custom-property list.
        (await _service.ListAsync(Asset, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_SeededWithOnlyStandardFields_ReturnsEmpty()
    {
        string seeded = await SeedWithStandardInfoAsync();

        (await _service.ListAsync(seeded, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_BlankPath_Throws()
    {
        var act = () => _service.ListAsync("  ", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Materialises a copy of the asset that has an <c>/Info</c> dictionary with standard fields
    /// (Title/Author/Producer) via the existing metadata-edit service, so custom-property writes have an
    /// <c>/Info</c> object to update.
    /// </summary>
    private async Task<string> SeedWithStandardInfoAsync()
    {
        string seeded = TargetPath();
        await _seeder.EditAsync(
            Asset, seeded,
            new PdfMetadataSpec(Title: "My Title", Author: "Jane Doe", Producer: "Foliant Producer"),
            default);
        return seeded;
    }

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
