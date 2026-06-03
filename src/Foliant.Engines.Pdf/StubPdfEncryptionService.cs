using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Заглушка <see cref="IPdfEncryptionService"/>: валидирует аргументы и бросает
/// <see cref="System.NotSupportedException"/>. Существует, чтобы зафиксировать
/// точку расширения для Q-F30/F31 без обязательств по runtime в текущей сборке.
///
/// <para>Почему stub, а не реальный backend (Phase 1 решение):
/// <list type="bullet">
/// <item><b>PdfPig 0.1.10</b> (текущая версия в <c>Directory.Packages.props</c>) НЕ поддерживает
/// запись encryption — <c>PdfDocumentBuilder</c> создаёт только открытые документы. Чтение
/// зашифрованных есть (через <c>ParsingOptions.Password</c>), запись — нет.</item>
/// <item><b>QPDF embed</b> (Вариант B) даёт production-quality результат «из коробки», но
/// добавляет ~5 MB нативного бинаря в инсталлятор + cross-platform упаковка (qpdf.exe /
/// linux ELF / mac mach-o), которую надо согласовать с installer-репозиторием.</item>
/// <item><b>Raw cos-write через BouncyCastle</b> (Вариант C) — managed-only, нулевой footprint,
/// но требует написать ~600-800 строк encryption-dict / stream-AES-handler / R=6 password
/// algorithm (ISO 32000-2 §7.6.4) + золотые тесты против Acrobat — это 1-2 спринта.</item>
/// </list>
/// Поэтому Phase 1 фиксирует контракт (домен + port) + заглушку, а Phase 3 выберет B vs C
/// после сравнения footprint и dev-стоимости. Пока stub явно сигнализирует caller'у
/// (<see cref="System.NotSupportedException"/> с указанием Q-тикета), что feature не подключена.</para>
/// </summary>
public sealed class StubPdfEncryptionService : IPdfEncryptionService
{
    /// <summary>Сообщение, которое caller увидит в exception'е — содержит привязку к Q-F30/F31,
    /// чтобы grep по логам/issue tracker'у быстро поднимал контекст.</summary>
    public const string UnsupportedMessage =
        "PDF encryption backend is not wired in this build (Q-F30/F31, Phase 3 decision pending). " +
        "Choose QPDF embed or raw cos-write before enabling this surface.";

    /// <inheritdoc />
    public System.Threading.Tasks.Task EncryptAsync(string sourcePath, string targetPath, PdfEncryptionSpec spec, System.Threading.CancellationToken ct)
    {
        // Argument-валидация до NotSupported — caller получит конкретную ArgumentException
        // на «грязные» вызовы (мисcклик, тесты), и явный NotSupported на корректные.
        System.ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        System.ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        throw new System.NotSupportedException(UnsupportedMessage);
    }
}
