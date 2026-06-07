namespace Foliant.Application.Services;

/// <summary>
/// Редактирование OCG (Optional Content Groups, «PDF layers»): переименование и удаление
/// конкретного слоя инкрементальным апдейтом в новый файл (PDF spec §8.11). Дополняет
/// <see cref="IPdfOcgService"/> (чтение + toggle видимости): тот не покрывает rename/delete,
/// они вынесены сюда отдельным контрактом, чтобы не трогать существующую поверхность.
///
/// Реализации — managed-only (PdfPig + raw cos-write), без зависимости от native PDFium
/// OCG-bindings (которых нет в PDFiumCore 146.x). Оригинал не мутируется (паттерн
/// watermark/redact): результат пишется атомарно (temp + Move) в <c>targetPath</c>, при
/// этом <c>sourcePath == targetPath</c> безопасен (source читается в память до записи).
///
/// <para><b>Rename</b> переписывает <c>/Name</c> у целевого OCG-объекта. <b>Delete</b>
/// убирает ссылку на OCG из <c>/OCProperties → /OCGs</c> и из default-config <c>/D</c>
/// (<c>/ON</c>/<c>/OFF</c>/<c>/Order</c>). Контент, помеченный удалённым слоём через
/// маркеры <c>BDC</c>/<c>EMC</c>, становится <b>всегда видимым</b> (OCG больше не
/// объявлен) — это допустимо для MVP и согласуется с поведением Acrobat при удалении
/// слоя «без объектов»; редактирование content-stream'ов не выполняется.</para>
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустые / blank пути → <see cref="ArgumentException"/>.</item>
/// <item><c>layerIndex</c> вне диапазона <c>[0, layerCount)</c> →
/// <see cref="ArgumentOutOfRangeException"/>.</item>
/// <item>Пустое / blank <c>newName</c> (только для rename) → <see cref="ArgumentException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
/// </summary>
public interface IPdfOcgEditService
{
    /// <summary>Переименовывает слой с 0-based индексом <paramref name="layerIndex"/> (индекс в
    /// массиве <c>/OCProperties → /OCGs</c>, совпадает с <see cref="Domain.PdfLayer.Index"/>),
    /// записывая результат в <paramref name="targetPath"/> (atomic temp + Move). Оригинал не
    /// мутируется; <paramref name="sourcePath"/> == <paramref name="targetPath"/> безопасен.</summary>
    /// <param name="sourcePath">Путь к исходному PDF. Blank → <see cref="ArgumentException"/>.</param>
    /// <param name="targetPath">Путь, куда записать обновлённый PDF. Blank → <see cref="ArgumentException"/>.</param>
    /// <param name="layerIndex">0-based индекс слоя. Вне диапазона →
    /// <see cref="ArgumentOutOfRangeException"/>.</param>
    /// <param name="newName">Новое имя слоя. Blank → <see cref="ArgumentException"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача, завершающаяся по окончании записи.</returns>
    Task RenameAsync(string sourcePath, string targetPath, int layerIndex, string newName, CancellationToken ct);

    /// <summary>Удаляет слой с 0-based индексом <paramref name="layerIndex"/> (индекс в массиве
    /// <c>/OCProperties → /OCGs</c>): убирает его ссылку из <c>/OCGs</c> и из default-config
    /// <c>/D</c> (<c>/ON</c>/<c>/OFF</c>/<c>/Order</c>). Результат пишется в
    /// <paramref name="targetPath"/> (atomic temp + Move); оригинал не мутируется;
    /// <paramref name="sourcePath"/> == <paramref name="targetPath"/> безопасен.</summary>
    /// <param name="sourcePath">Путь к исходному PDF. Blank → <see cref="ArgumentException"/>.</param>
    /// <param name="targetPath">Путь, куда записать обновлённый PDF. Blank → <see cref="ArgumentException"/>.</param>
    /// <param name="layerIndex">0-based индекс слоя. Вне диапазона →
    /// <see cref="ArgumentOutOfRangeException"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача, завершающаяся по окончании записи.</returns>
    Task RemoveAsync(string sourcePath, string targetPath, int layerIndex, CancellationToken ct);
}
