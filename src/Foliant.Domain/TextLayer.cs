namespace Foliant.Domain;

public sealed record TextRun(string Text, double X, double Y, double W, double H);

public sealed record TextLayer(int PageIndex, IReadOnlyList<TextRun> Runs)
{
    public static TextLayer Empty(int pageIndex) => new(pageIndex, []);

    public string ToPlainText() => string.Concat(Runs.Select(r => r.Text));
}
