namespace Foliant.Domain;

/// <summary>
/// Описание обрезки страниц PDF — доли (0..0.5) ширины/высоты, которые срезаются с каждой
/// стороны. Pure-data, без I/O — потребляется <c>IPdfCropService</c>. Применяется ко всем
/// страницам единообразно (per-range — follow-up).
///
/// Семантика обратимая: реализация выставляет <c>/CropBox</c> поверх существующего
/// <c>/MediaBox</c>, поэтому контент не теряется — другой viewer может вернуть исходные
/// границы. Физический crop (с обрезкой контента) — Phase 2 (см. Q-F15).
/// </summary>
/// <param name="Left">Доля ширины страницы, срезаемая слева (0..0.5).</param>
/// <param name="Top">Доля высоты страницы, срезаемая сверху (0..0.5).</param>
/// <param name="Right">Доля ширины страницы, срезаемая справа (0..0.5).</param>
/// <param name="Bottom">Доля высоты страницы, срезаемая снизу (0..0.5).</param>
public sealed record CropSpec(double Left, double Top, double Right, double Bottom)
{
    /// <summary>True если хотя бы одна сторона срезается на ≥ 0.5 %; иначе spec — no-op
    /// и сервису незачем переписывать документ.</summary>
    public bool HasEffect =>
        Left > 0.005 || Top > 0.005 || Right > 0.005 || Bottom > 0.005;

    /// <summary>Бросает <see cref="ArgumentOutOfRangeException"/>, если любая сторона вне
    /// [0, 0.5] либо <c>Left+Right</c> / <c>Top+Bottom</c> ≥ 0.95 (после такого crop
    /// видимая область вырождается до полоски в 5 % — заведомо ошибка пользователя).</summary>
    public void Validate()
    {
        EnsureInRange(Left, nameof(Left));
        EnsureInRange(Top, nameof(Top));
        EnsureInRange(Right, nameof(Right));
        EnsureInRange(Bottom, nameof(Bottom));
        if (Left + Right >= 0.95)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Left), $"Left + Right must be < 0.95 (got {Left + Right:F3}).");
        }
        if (Top + Bottom >= 0.95)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Top), $"Top + Bottom must be < 0.95 (got {Top + Bottom:F3}).");
        }
    }

    private static void EnsureInRange(double value, string name)
    {
        if (double.IsNaN(value) || value < 0.0 || value > 0.5)
        {
            throw new ArgumentOutOfRangeException(name, value, "Each side must be in [0, 0.5].");
        }
    }
}
