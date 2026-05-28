namespace Foliant.Domain;

/// <summary>Текстовая строка в слое (OCR или vector-text). <see cref="Confidence"/> — 0..1 для
/// OCR-движков, <c>1.0</c> для vector-text (там всё «достоверно»). Используется UI/экспортом для
/// тинтования низко-достоверных регионов и фильтрации.</summary>
public sealed record TextRun(string Text, double X, double Y, double W, double H, double Confidence = 1.0);

public sealed record TextLayer(int PageIndex, IReadOnlyList<TextRun> Runs)
{
    public static TextLayer Empty(int pageIndex) => new(pageIndex, []);

    public string ToPlainText() => string.Concat(Runs.Select(r => r.Text));
}
