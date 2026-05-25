using System.Text.Json;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf.Editing;
using Xunit;

namespace Foliant.Engines.Pdf.Tests.Editing;

/// <summary>
/// Pure unit tests (no native/IO): каждая команда сериализуется в
/// <see cref="DocumentCommandRecord"/> с правильным <c>Kind</c>, а payload
/// десериализуется обратно в эквивалентные параметры.
/// </summary>
public sealed class PdfCommandTests
{
    [Fact]
    public void RotatePageCommand_RoundTrips()
    {
        var cmd = new RotatePageCommand(3, ViewRotation.Cw90);

        var rec = cmd.ToRecord();

        rec.Kind.Should().Be("rotate-page");
        JsonSerializer.Deserialize<RotatePageCommand>(rec.PayloadJson).Should().Be(cmd);
    }

    [Fact]
    public void DeletePageCommand_RoundTrips()
    {
        var cmd = new DeletePageCommand(2);

        var rec = cmd.ToRecord();

        rec.Kind.Should().Be("delete-page");
        JsonSerializer.Deserialize<DeletePageCommand>(rec.PayloadJson).Should().Be(cmd);
    }

    [Fact]
    public void ReorderPagesCommand_RoundTrips()
    {
        var cmd = new ReorderPagesCommand([2, 0, 1]);

        var rec = cmd.ToRecord();

        rec.Kind.Should().Be("reorder-pages");
        var back = JsonSerializer.Deserialize<ReorderPagesCommand>(rec.PayloadJson);
        back!.Order.Should().Equal(2, 0, 1);
    }

    [Fact]
    public void InsertPagesCommand_RoundTrips()
    {
        var cmd = new InsertPagesCommand(@"C:\docs\other.pdf", 4);

        var rec = cmd.ToRecord();

        rec.Kind.Should().Be("insert-pages");
        JsonSerializer.Deserialize<InsertPagesCommand>(rec.PayloadJson).Should().Be(cmd);
    }

    [Fact]
    public void Kind_Property_MatchesRecord()
    {
        IDocumentCommand cmd = new DeletePageCommand(0);
        cmd.Kind.Should().Be(cmd.ToRecord().Kind);
    }
}
