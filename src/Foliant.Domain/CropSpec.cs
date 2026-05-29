namespace Foliant.Domain;

/// <summary>
/// Описание обрезки страниц PDF — доли (0..0.5) ширины/высоты, которые срезаются с каждой
/// стороны, плюс <see cref="CropMode"/> — обратимый или физический режим. Pure-data, без
/// I/O — потребляется <c>IPdfCropService</c>. Применяется ко всем страницам единообразно
/// (per-range — follow-up).
///
/// Режимы:
/// <list type="bullet">
/// <item><see cref="CropMode.Reversible"/> — реализация выставляет <c>/CropBox</c> поверх
///   существующего <c>/MediaBox</c>; контент не теряется, другой viewer (или сама Foliant
///   с обнулённым CropBox) может вернуть исходные границы.</item>
/// <item><see cref="CropMode.Physical"/> — реализация ставит и <c>/CropBox</c>, и
///   <c>/MediaBox</c> в новые границы И добавляет page-level clipping path. Содержимое
///   за новыми границами не рендерится никаким viewer'ом, размеры страницы фактически
///   уменьшаются. Контент-stream не переписывается (объекты остаются в файле), поэтому
///   полностью «удалить» их — Phase 2+; для большинства задач clip-based вариант
///   эквивалентен «настоящему» физическому crop'у.</item>
/// </list>
/// </summary>
/// <param name="Left">Доля ширины страницы, срезаемая слева (0..0.5).</param>
/// <param name="Top">Доля высоты страницы, срезаемая сверху (0..0.5).</param>
/// <param name="Right">Доля ширины страницы, срезаемая справа (0..0.5).</param>
/// <param name="Bottom">Доля высоты страницы, срезаемая снизу (0..0.5).</param>
/// <param name="Mode">Режим обрезки; default — <see cref="CropMode.Reversible"/>.</param>
public sealed record CropSpec(
    double Left,
    double Top,
    double Right,
    double Bottom,
    CropMode Mode = CropMode.Reversible)
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

/// <summary>Режим работы <see cref="CropSpec"/>: обратимый (только <c>/CropBox</c>) или
/// физический (<c>/MediaBox</c> + clip-path).</summary>
public enum CropMode
{
    /// <summary>Только <c>/CropBox</c> — viewer-level подсказка о видимой области.
    /// Полностью обратимо: достаточно убрать <c>/CropBox</c>, чтобы получить исходный документ.
    /// Default для обратной совместимости.</summary>
    Reversible,

    /// <summary>Физический crop: меняем и <c>/CropBox</c>, и <c>/MediaBox</c>, и добавляем
    /// page-level clipping path. Размеры страницы реально уменьшаются, контент за новыми
    /// границами не виден никаким viewer'ом. Объекты не удаляются из файла — это
    /// можно делать в Phase 2+, если потребуется уменьшить вес файла.</summary>
    Physical,
}
