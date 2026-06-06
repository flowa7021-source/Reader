namespace Foliant.Domain;

/// <summary>
/// Один диапазон page-label нумерации (PDF <c>/PageLabels</c> number-tree entry, ISO 32000-1
/// §12.4.2). Диапазон начинается со страницы <see cref="StartPageIndex"/> (0-based) и действует до
/// начала следующего диапазона (или до конца документа). Видимая метка страницы <c>p</c> внутри
/// диапазона: <c>Prefix + format(Start + (p - StartPageIndex), Style)</c> — см.
/// <see cref="PdfPageLabelFormatter"/>.
///
/// <para>Pure-data, immutable. Создавать через <see cref="Create"/> (валидация + нормализация):
/// пустой <see cref="Prefix"/> приводится к <see langword="null"/>, а для
/// <see cref="PdfPageLabelStyle.None"/> <see cref="Start"/> нормализуется к 1 (числовой части нет),
/// чтобы запись и чтение через <c>IPdfPageLabelService</c> были симметричны. <c>with</c>-копии идут
/// мимо валидации — это допустимо, как и у других domain-record'ов.</para>
/// </summary>
/// <param name="StartPageIndex">0-based индекс страницы, с которой начинается диапазон.</param>
/// <param name="Style">Стиль числовой части (<c>/S</c>).</param>
/// <param name="Prefix">Префикс метки (<c>/P</c>) или <see langword="null"/>, если префикса нет.
/// Пробелы значимы и сохраняются (например, «A-» для меток «A-1», «A-2»).</param>
/// <param name="Start">Первое числовое значение в диапазоне (<c>/St</c>, ≥ 1). Для
/// <see cref="PdfPageLabelStyle.None"/> игнорируется (нормализуется к 1).</param>
public sealed record PdfPageLabelRange(
    int StartPageIndex,
    PdfPageLabelStyle Style,
    string? Prefix,
    int Start)
{
    /// <summary>
    /// Создаёт диапазон с валидацией инвариантов и нормализацией (пустой префикс → <see langword="null"/>;
    /// <see cref="Start"/> → 1 для стиля <see cref="PdfPageLabelStyle.None"/>).
    /// </summary>
    /// <param name="startPageIndex">0-based индекс первой страницы диапазона (≥ 0).</param>
    /// <param name="style">Стиль числовой части.</param>
    /// <param name="prefix">Необязательный префикс; <see langword="null"/> или пустая строка = без префикса.</param>
    /// <param name="start">Стартовое числовое значение (≥ 1); по умолчанию 1.</param>
    /// <returns>Валидный нормализованный <see cref="PdfPageLabelRange"/>.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="startPageIndex"/> &lt; 0,
    /// <paramref name="start"/> &lt; 1, либо <paramref name="style"/> вне диапазона enum'а.</exception>
    public static PdfPageLabelRange Create(
        int startPageIndex,
        PdfPageLabelStyle style,
        string? prefix = null,
        int start = 1)
    {
        System.ArgumentOutOfRangeException.ThrowIfNegative(startPageIndex);
        System.ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        if (!System.Enum.IsDefined(style))
        {
            throw new System.ArgumentOutOfRangeException(nameof(style), style, "Unknown page-label style.");
        }

        return new PdfPageLabelRange(
            startPageIndex,
            style,
            string.IsNullOrEmpty(prefix) ? null : prefix,
            style == PdfPageLabelStyle.None ? 1 : start);
    }
}
