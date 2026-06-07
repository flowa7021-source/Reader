using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Читает <b>богатое</b> PDF /Outlines (содержание) целиком — со всеми атрибутами узлов: режим
/// отображения целевой страницы, стиль (жирный/курсив), цвет заголовка, развёрнут/свёрнут — в плоский
/// список <see cref="DocumentOutlineEntry"/> с пометкой глубины. Это симметричная read-сторона
/// rich-writer'а (<see cref="IPdfOutlineWriter"/>): то, что writer записал в /Outlines, inspector
/// читает обратно без потерь.
///
/// <para>В отличие от <see cref="IPdfOutlineReader"/> (который опускает узлы без страничного
/// destination — он нужен для импорта в закладки), inspector показывает <b>всё</b> дерево как есть:
/// узел с неразрешимой страницей всё равно попадает в результат с <see cref="DocumentOutlineEntry.PageIndex"/>
/// = <c>-1</c>. Предназначен для инспектора структуры документа, а не для импорта закладок.</para>
///
/// <para>Stateless; делегирует чтение реализации.</para>
/// </summary>
public interface IPdfOutlineInspector
{
    /// <summary>
    /// Прочитать богатое /Outlines из PDF в pre-order (узел → его дети → следующий сиблинг), сохраняя
    /// порядок и глубину. Best-effort: битый / отсутствующий /Outlines → пустой список, исключения
    /// (кроме отмены) не пробрасываются.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF-файлу.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Плоский список узлов outline'а с глубиной и rich-атрибутами; пустой, если outline
    /// отсутствует или PDF не читается.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<DocumentOutlineEntry>> ReadRichAsync(string pdfPath, CancellationToken ct);
}
