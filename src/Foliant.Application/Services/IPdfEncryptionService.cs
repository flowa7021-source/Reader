using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Port для AES-256 шифрования PDF (Q-F30) с восемью permission-флагами (Q-F31).
/// Stateless; реализация открывает source-файл, накладывает encryption dictionary
/// (PDF 2.0 / Acrobat X-XI compatible) и записывает результат в target-файл атомарно
/// (temp + Move, как соседние сервисы — см. <see cref="IPdfSplitService"/>).
/// Decrypt-обратная цепочка В ЭТОТ port НЕ ВХОДИТ (она строится через диалог пароля
/// при открытии — другая UX-нить).
///
/// <para>Контракт ошибок:
/// <list type="bullet">
/// <item>null/whitespace пути или <c>spec</c> = null → <see cref="System.ArgumentException"/>
/// / <see cref="System.ArgumentNullException"/>.</item>
/// <item>Битый PDF / IO-сбой → проброс <see cref="System.IO.IOException"/> или
/// <see cref="System.InvalidOperationException"/>.</item>
/// <item>Реализация-stub (отсутствует backend) → <see cref="System.NotSupportedException"/>
/// с текстом, объясняющим почему функционал недоступен в текущей сборке.</item>
/// </list>
/// </para>
///
/// <para>UI/DI-биндинг — отдельный PR; здесь только port + домен-типы и заглушка.</para>
/// </summary>
public interface IPdfEncryptionService
{
    /// <summary>
    /// Шифрует <paramref name="sourcePath"/> по <paramref name="spec"/> и пишет результат
    /// в <paramref name="targetPath"/>. Реализация атомарна (temp + Move), source не
    /// модифицируется.
    /// </summary>
    System.Threading.Tasks.Task EncryptAsync(string sourcePath, string targetPath, PdfEncryptionSpec spec, System.Threading.CancellationToken ct);
}
