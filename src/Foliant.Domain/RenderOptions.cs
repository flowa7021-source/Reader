namespace Foliant.Domain;

public sealed record RenderOptions(
    double Zoom,
    int? MaxWidthPx = null,
    int? MaxHeightPx = null,
    bool RenderAnnotations = true,
    RenderTheme Theme = RenderTheme.Original,
    ViewRotation Rotation = ViewRotation.None)
{
    public static RenderOptions Default { get; } = new(Zoom: 1.0);

    public RenderOptions WithZoom(double zoom) => this with { Zoom = zoom };

    public RenderOptions WithRotation(ViewRotation rotation) => this with { Rotation = rotation };

    public int ZoomBucket() => (int)Math.Round(Zoom * 100 / 25.0) * 25;
}
