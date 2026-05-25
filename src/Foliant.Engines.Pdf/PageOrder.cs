namespace Foliant.Engines.Pdf;

/// <summary>
/// Чистая (без зависимости от PdfPig) арифметика над порядком страниц.
/// Все методы принимают 0-based индексы (как в публичном API <see cref="PdfPageOps"/>)
/// и возвращают <b>1-based</b> номера страниц — формат, который ожидает
/// PdfPig (<c>PdfMerger.Merge(..., pagesBundle)</c> / <c>PdfDocumentBuilder.AddPage(doc, pageNumber)</c>).
///
/// Выделено отдельно, чтобы покрыть перестановки/удаление/вставку unit-тестами,
/// не загружая нативный слой.
///
/// Публичный (а не internal) — чтобы тестовый проект покрывал арифметику без
/// <c>InternalsVisibleTo</c> (этот репозиторий его не настраивает).
/// </summary>
public static class PageOrder
{
    /// <summary>
    /// Последовательность 1-based номеров всех страниц <paramref name="pageCount"/>
    /// кроме удаляемой <paramref name="deleteIndex"/> (0-based), порядок сохраняется.
    /// </summary>
    public static int[] BuildAfterDelete(int pageCount, int deleteIndex)
    {
        ValidatePageCount(pageCount);
        if (deleteIndex < 0 || deleteIndex >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deleteIndex), deleteIndex,
                $"Delete index must be in [0, {pageCount - 1}].");
        }

        var result = new int[pageCount - 1];
        int w = 0;
        for (int i = 0; i < pageCount; i++)
        {
            if (i == deleteIndex)
            {
                continue;
            }

            result[w++] = i + 1; // → 1-based
        }

        return result;
    }

    /// <summary>
    /// Переставляет страницы согласно <paramref name="newOrder"/> (массив 0-based индексов,
    /// который обязан быть перестановкой <c>0..pageCount-1</c>). Возвращает 1-based номера
    /// в заданном порядке.
    /// </summary>
    public static int[] BuildReorder(int pageCount, int[] newOrder)
    {
        ValidatePageCount(pageCount);
        ArgumentNullException.ThrowIfNull(newOrder);
        if (newOrder.Length != pageCount)
        {
            throw new ArgumentException(
                $"newOrder length {newOrder.Length} must equal page count {pageCount}.",
                nameof(newOrder));
        }

        var seen = new bool[pageCount];
        var result = new int[pageCount];
        for (int i = 0; i < newOrder.Length; i++)
        {
            int src = newOrder[i];
            ValidatePermutationMember(newOrder, seen, src, i, pageCount);
            seen[src] = true;
            result[i] = src + 1; // → 1-based
        }

        return result;
    }

    private static void ValidatePermutationMember(int[] newOrder, bool[] seen, int src, int i, int pageCount)
    {
        if (src < 0 || src >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newOrder), src, $"newOrder[{i}] must be in [0, {pageCount - 1}].");
        }

        if (seen[src])
        {
            throw new ArgumentException(
                $"newOrder contains duplicate index {src}; must be a permutation.", nameof(newOrder));
        }
    }

    /// <summary>
    /// Нормализует позицию вставки: допускает <c>[0, pageCount]</c> (включая «в конец»).
    /// </summary>
    public static int ResolveInsertPosition(int pageCount, int atIndex)
    {
        ValidatePageCount(pageCount, allowZero: true);
        if (atIndex < 0 || atIndex > pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atIndex), atIndex,
                $"Insert index must be in [0, {pageCount}] (inclusive end).");
        }

        return atIndex;
    }

    /// <summary>
    /// 1-based номера базового документа, которые идут <b>до</b> точки вставки.
    /// Пустой массив, если вставка в начало.
    /// </summary>
    public static int[] BasePagesBefore(int pageCount, int atIndex)
    {
        int pos = ResolveInsertPosition(pageCount, atIndex);
        return Range1Based(1, pos);
    }

    /// <summary>
    /// 1-based номера базового документа, которые идут <b>после</b> точки вставки.
    /// Пустой массив, если вставка в конец.
    /// </summary>
    public static int[] BasePagesAfter(int pageCount, int atIndex)
    {
        int pos = ResolveInsertPosition(pageCount, atIndex);
        return Range1Based(pos + 1, pageCount - pos);
    }

    /// <summary>1-based номера страниц <c>1..count</c> вставляемого документа.</summary>
    public static int[] AllPages(int count)
    {
        ValidatePageCount(count, allowZero: true);
        return Range1Based(1, count);
    }

    private static int[] Range1Based(int start, int length)
    {
        if (length <= 0)
        {
            return [];
        }

        var result = new int[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = start + i;
        }

        return result;
    }

    private static void ValidatePageCount(int pageCount, bool allowZero = false)
    {
        int min = allowZero ? 0 : 1;
        if (pageCount < min)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageCount), pageCount,
                allowZero ? "Page count cannot be negative." : "Page count must be positive.");
        }
    }
}
