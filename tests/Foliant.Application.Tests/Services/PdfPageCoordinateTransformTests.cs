using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class PdfPageCoordinateTransformTests
{
    // 612 × 792 в PDF user space (Letter). Корнеры — pdfW/pdfH в формулах.
    private const double PdfW = 612;
    private const double PdfH = 792;

    private static readonly PdfPageBox Origin0Rot0 = new(0, 0, PdfW, PdfH, 0);
    private static readonly PdfPageBox Origin0Rot90 = new(0, 0, PdfW, PdfH, 1);
    private static readonly PdfPageBox Origin0Rot180 = new(0, 0, PdfW, PdfH, 2);
    private static readonly PdfPageBox Origin0Rot270 = new(0, 0, PdfW, PdfH, 3);

    [Fact]
    public void TransformPoint_NoRotateNoOffset_IsIdentity()
    {
        var (x, y) = PdfPageCoordinateTransform.TransformPoint(100, 200, Origin0Rot0);

        x.Should().Be(100);
        y.Should().Be(200);
    }

    [Fact]
    public void TransformPoint_MediaBoxOriginOffset_AddsOffset()
    {
        // PDF с MediaBox = [100 200 712 992] — non-zero origin.
        var box = new PdfPageBox(100, 200, 712, 992, 0);

        var (x, y) = PdfPageCoordinateTransform.TransformPoint(50, 60, box);

        x.Should().Be(150);
        y.Should().Be(260);
    }

    [Theory]
    // R=90: display BL (0,0) → PDF top-left (0, pdfH). Display BR (pdfH, 0) → PDF BL (0, 0).
    [InlineData(0, 0, 0, PdfH)]
    [InlineData(PdfH, 0, 0, 0)]
    [InlineData(PdfH, PdfW, PdfW, 0)]
    [InlineData(0, PdfW, PdfW, PdfH)]
    public void TransformPoint_Rot90_MapsDisplayCornersToPdfCorners(
        double xDisp, double yDisp, double xPdf, double yPdf)
    {
        var (x, y) = PdfPageCoordinateTransform.TransformPoint(xDisp, yDisp, Origin0Rot90);

        x.Should().BeApproximately(xPdf, 0.001);
        y.Should().BeApproximately(yPdf, 0.001);
    }

    [Theory]
    // R=180: display BL → PDF TR, display TR → PDF BL (axes flipped).
    [InlineData(0, 0, PdfW, PdfH)]
    [InlineData(PdfW, PdfH, 0, 0)]
    public void TransformPoint_Rot180_FlipsBothAxes(
        double xDisp, double yDisp, double xPdf, double yPdf)
    {
        var (x, y) = PdfPageCoordinateTransform.TransformPoint(xDisp, yDisp, Origin0Rot180);

        x.Should().BeApproximately(xPdf, 0.001);
        y.Should().BeApproximately(yPdf, 0.001);
    }

    [Theory]
    // R=270: display BL (0,0) → PDF BR (pdfW, 0). Display TL (0, pdfW) → PDF BL (0, 0).
    [InlineData(0, 0, PdfW, 0)]
    [InlineData(PdfH, 0, PdfW, PdfH)]
    [InlineData(PdfH, PdfW, 0, PdfH)]
    [InlineData(0, PdfW, 0, 0)]
    public void TransformPoint_Rot270_MapsDisplayCornersToPdfCorners(
        double xDisp, double yDisp, double xPdf, double yPdf)
    {
        var (x, y) = PdfPageCoordinateTransform.TransformPoint(xDisp, yDisp, Origin0Rot270);

        x.Should().BeApproximately(xPdf, 0.001);
        y.Should().BeApproximately(yPdf, 0.001);
    }

    [Fact]
    public void TransformRect_Rot90_SwapsWidthAndHeight()
    {
        // Display rect (10, 20) size 30×40 at bottom-left area.
        var r = new PdfRect(10, 20, 40, 60);

        var t = PdfPageCoordinateTransform.TransformRect(r, Origin0Rot90);

        // Под /Rotate=90 W↔H меняются местами: 40×30. Углы пересчитаны и приведены к LL/UR.
        (t.XUR - t.XLL).Should().BeApproximately(40, 0.001);
        (t.YUR - t.YLL).Should().BeApproximately(30, 0.001);
    }

    [Fact]
    public void TransformRect_Rot0_IsIdentity()
    {
        var r = new PdfRect(10, 20, 40, 60);

        PdfPageCoordinateTransform.TransformRect(r, Origin0Rot0).Should().Be(r);
    }

    [Fact]
    public void TransformRect_NonOriginMediaBox_OffsetsOnly()
    {
        var box = new PdfPageBox(100, 200, 712, 992, 0);
        var r = new PdfRect(10, 20, 40, 60);

        var t = PdfPageCoordinateTransform.TransformRect(r, box);

        t.Should().Be(new PdfRect(110, 220, 140, 260));
    }

    [Fact]
    public void ToUserSpace_TransformsQuadPointsAndInkPointsAlongWithRect()
    {
        var spec = new PdfAnnotationSpec(
            PageIndex: 0,
            Subtype: PdfAnnotationSubtype.Highlight,
            Rect: new PdfRect(10, 20, 40, 60),
            Color: new PdfRgba(255, 0, 0, 102),
            QuadPoints: new double[] { 10, 60, 40, 60, 10, 20, 40, 20 },
            InkPoints: [new AnnotationPoint(10, 20), new AnnotationPoint(40, 60)]);

        var transformed = PdfPageCoordinateTransform.ToUserSpace(spec, Origin0Rot180);

        // Rect перевернут (BL ↔ TR), затем пересортирован в LL/UR через min/max.
        transformed.Rect.Should().Be(new PdfRect(PdfW - 40, PdfH - 60, PdfW - 10, PdfH - 20));
        // Каждый quad-point пересчитан тем же transform'ом.
        var qp = transformed.QuadPoints!;
        qp.Should().HaveCount(8);
        qp[0].Should().BeApproximately(PdfW - 10, 0.001);
        qp[1].Should().BeApproximately(PdfH - 60, 0.001);
        // Ink-точки тоже.
        transformed.InkPoints!.Should().Equal(
            new AnnotationPoint(PdfW - 10, PdfH - 20),
            new AnnotationPoint(PdfW - 40, PdfH - 60));
    }

    [Fact]
    public void TransformPoint_FourQuarterTurns_ReturnsToOriginal()
    {
        // Композиция четырёх 90°-поворотов вокруг центра страницы — identity. Это проверяет, что
        // формула самосогласована: применяя R=1 четырежды на одной и той же квадратной box'ке
        // (pdfW=pdfH, иначе после двух turn'ов dim'ы не совпадут), возвращаемся к исходной точке.
        var square = new PdfPageBox(0, 0, 500, 500, 1);
        double x = 123.5;
        double y = 456.25;
        for (int i = 0; i < 4; i++)
        {
            (x, y) = PdfPageCoordinateTransform.TransformPoint(x, y, square);
        }

        x.Should().BeApproximately(123.5, 0.001);
        y.Should().BeApproximately(456.25, 0.001);
    }
}
