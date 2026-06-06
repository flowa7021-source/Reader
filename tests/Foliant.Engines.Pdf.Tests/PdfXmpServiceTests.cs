using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigXmpService"/>: write a document-level XMP packet
/// into the catalog <c>/Metadata</c> stream of the 10-page asset, then read it back through the same
/// production service and assert fidelity (including UTF-8 Unicode payloads and packet replacement).
/// Pure managed PdfPig — no native runtime — so no Slow trait (mirrors
/// <see cref="PdfAttachmentServiceTests"/>).
/// </summary>
public sealed class PdfXmpServiceTests : IDisposable
{
    private const string SamplePacket =
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Foliant\">" +
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
        "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Sample Title</rdf:li></rdf:Alt></dc:title>" +
        "</rdf:Description></rdf:RDF></x:xmpmeta>" +
        "<?xpacket end=\"w\"?>";

    private readonly string _tmpDir;
    private readonly PdfPigXmpService _service = new(NullLogger<PdfPigXmpService>.Instance);

    public PdfXmpServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-xmp-" + Guid.NewGuid().ToString("N"));
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
    public async Task Write_ThenRead_ReturnsIdenticalPacket()
    {
        string target = TargetPath();

        await _service.WriteAsync(Asset, target, SamplePacket, default);

        (await _service.ReadAsync(target, default)).Should().Be(SamplePacket);
    }

    [Fact]
    public async Task Write_UnicodePacket_RoundTripsThroughUtf8()
    {
        string target = TargetPath();
        string packet = MakePacket("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Заголовок документа</rdf:li></rdf:Alt></dc:title>");

        await _service.WriteAsync(Asset, target, packet, default);

        (await _service.ReadAsync(target, default)).Should().Be(packet, "Cyrillic must survive UTF-8 round-trip");
    }

    [Fact]
    public async Task Write_EmptyPacket_RoundTripsAsEmpty()
    {
        string target = TargetPath();

        await _service.WriteAsync(Asset, target, string.Empty, default);

        (await _service.ReadAsync(target, default)).Should().Be(string.Empty);
    }

    [Fact]
    public async Task Write_Twice_SecondPacketWins()
    {
        string target = TargetPath();
        await _service.WriteAsync(Asset, target, SamplePacket, default);

        string second = MakePacket("<dc:creator><rdf:Seq><rdf:li>Second Author</rdf:li></rdf:Seq></dc:creator>");
        await _service.WriteAsync(target, target, second, default); // source == target on replace.

        (await _service.ReadAsync(target, default)).Should().Be(second, "the latest write replaces the packet");
    }

    [Fact]
    public async Task Read_RawAsset_ReturnsNull()
    {
        (await _service.ReadAsync(Asset, default)).Should().BeNull("the asset has no /Metadata");
    }

    [Fact]
    public async Task Write_PreservesPageCount()
    {
        string target = TargetPath();

        await _service.WriteAsync(Asset, target, SamplePacket, default);

        PageCount(target).Should().Be(PageCount(Asset));
        PageCount(target).Should().Be(10);
    }

    [Fact]
    public async Task Write_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);

        await _service.WriteAsync(Asset, target, SamplePacket, default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankSource_Throws(string blank)
    {
        var act = () => _service.WriteAsync(blank, TargetPath(), SamplePacket, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankTarget_Throws(string blank)
    {
        var act = () => _service.WriteAsync(Asset, blank, SamplePacket, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Write_NullPacket_Throws()
    {
        var act = () => _service.WriteAsync(Asset, TargetPath(), null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Read_BlankPath_Throws(string blank)
    {
        var act = () => _service.ReadAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static string MakePacket(string body) =>
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Foliant\">" +
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
        body +
        "</rdf:Description></rdf:RDF></x:xmpmeta>" +
        "<?xpacket end=\"w\"?>";

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
