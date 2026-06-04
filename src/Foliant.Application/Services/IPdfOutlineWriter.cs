using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Встраивает плоский список <see cref="DocumentOutlineEntry"/> обратно в PDF /Outlines
/// (содержание), чтобы закладки стали видны в Acrobat / любом стороннем viewer'е — симметрично
/// <see cref="IPdfOutlineReader"/>. Наш sidecar bookmark-store знает о закладках, но сам PDF —
/// нет, пока пользователь явно не запросит экспорт. Дерево строится из <see
/// cref="DocumentOutlineEntry.Depth"/> (0 = top-level), каждый узел получает <c>/Dest [page /Fit]</c>.
///
/// Stateless; оригинал не мутируется — результат пишется в <c>targetPath</c> (атомарно: temp+Move).
/// Делегирует cos-сериализацию реализации.
/// </summary>
public interface IPdfOutlineWriter
{
    /// <summary>
    /// Прочитать <paramref name="sourcePath"/>, дописать инкрементальным апдейтом /Outlines из
    /// <paramref name="entries"/> и записать результат в <paramref name="targetPath"/>. Оригинал
    /// остаётся байт-в-байт неизменным (новые объекты дописываются в конец, см. ISO 32000-1 §7.5.6).
    /// </summary>
    /// <param name="sourcePath">Путь к исходному PDF. Не модифицируется.</param>
    /// <param name="targetPath">Куда записать PDF с outline'ом. Может совпадать с
    /// <paramref name="sourcePath"/> (исходник читается целиком до записи temp+Move).</param>
    /// <param name="entries">Плоский список с глубиной. Пустой список → валидный PDF <b>без</b>
    /// /Outlines (исходный outline, если был, отбрасывается инкрементальной заменой catalog'а).
    /// <see cref="DocumentOutlineEntry.PageIndex"/> вне диапазона страниц <b>зажимается</b> (clamp)
    /// в <c>[0, pageCount-1]</c> — узел всегда указывает на валидную страницу, а не теряется.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> или
    /// <paramref name="targetPath"/> пуст / whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="entries"/> равен
    /// <see langword="null"/>.</exception>
    Task WriteOutlineAsync(string sourcePath, string targetPath, IReadOnlyList<DocumentOutlineEntry> entries, CancellationToken ct);
}
