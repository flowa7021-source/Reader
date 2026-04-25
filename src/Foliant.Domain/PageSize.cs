namespace Foliant.Domain;

public readonly record struct PageSize(double WidthPt, double HeightPt)
{
    public double AspectRatio => HeightPt == 0 ? 0 : WidthPt / HeightPt;

    public PageSize Rotate(int quarterTurns) =>
        (quarterTurns % 2) == 0 ? this : new PageSize(HeightPt, WidthPt);
}
