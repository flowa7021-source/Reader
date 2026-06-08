using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfPreflightService"/>. Тонкий <b>orchestrator</b>: не реализует cos-логику
/// заново, а композирует уже готовые инспекторы (<see cref="IPdfFontService"/>,
/// <see cref="IPdfSanitizationService"/>, <see cref="IPdfOutputIntentService"/>,
/// <see cref="IPdfLinkService"/>) и добавляет структурную выжимку из PdfPig (число страниц, версия,
/// шифрование, наличие извлекаемого текста). Зеркалит форму <see cref="PdfPigFontService"/>:
/// валидация аргументов, offload структурного чтения в
/// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>, логирование. Чтение best-effort:
/// битый / отсутствующий / зашифрованный без пароля PDF → структурные поля с дефолтами (страниц 0,
/// версия <c>""</c>, не зашифрован, текста нет), при этом поля от под-сервисов всё равно возвращаются.
/// Только чтение, записи нет.
/// </summary>
public sealed class PdfPigPreflightService : IPdfPreflightService
{
    /// <summary>
    /// Максимум страниц, сканируемых на наличие извлекаемого текста. Для image-only (сканов) текста нет
    /// ни на одной странице, поэтому без капа пришлось бы проходить весь документ; на текстовых же PDF
    /// первая страница обычно срабатывает сразу. Кап ограничивает worst-case большого скана.
    /// </summary>
    private const int TextScanPageCap = 50;

    private readonly IPdfFontService _fonts;
    private readonly IPdfSanitizationService _sanitization;
    private readonly IPdfOutputIntentService _outputIntents;
    private readonly IPdfLinkService _links;
    private readonly ILogger<PdfPigPreflightService> _log;

    /// <summary>Создаёт сервис, инъектируя существующие инспекторы и логгер для best-effort диагностики.</summary>
    /// <param name="fonts">Инспектор шрифтов — даёт <c>FontCount</c> / <c>NonEmbeddedFontCount</c>.</param>
    /// <param name="sanitization">Сканер скриптов / действий — даёт <c>HasJavaScriptOrActions</c>.</param>
    /// <param name="outputIntents">Лист output-intent'ов — даёт <c>OutputIntentCount</c> / <c>HasIccOutputIntent</c>.</param>
    /// <param name="links">Лист link-аннотаций — даёт <c>LinkCount</c>.</param>
    /// <param name="log">Логгер для best-effort диагностики структурного чтения.</param>
    public PdfPigPreflightService(
        IPdfFontService fonts,
        IPdfSanitizationService sanitization,
        IPdfOutputIntentService outputIntents,
        IPdfLinkService links,
        ILogger<PdfPigPreflightService> log)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(sanitization);
        ArgumentNullException.ThrowIfNull(outputIntents);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(log);

        _fonts = fonts;
        _sanitization = sanitization;
        _outputIntents = outputIntents;
        _links = links;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<PdfPreflightReport> PreflightAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        // Composition: каждый под-инспектор сам best-effort (битый PDF → пустой результат, без throw'а),
        // отмену пробрасываем.
        var fonts = await _fonts.ListFontsAsync(pdfPath, ct).ConfigureAwait(false);
        var scan = await _sanitization.ScanAsync(pdfPath, ct).ConfigureAwait(false);
        var intents = await _outputIntents.ListAsync(pdfPath, ct).ConfigureAwait(false);
        var links = await _links.ListLinksAsync(pdfPath, ct).ConfigureAwait(false);

        // Структурная выжимка из PdfPig — offload + best-effort (см. ReadStructuralSafe).
        StructuralInfo structural = await Task.Run(() => ReadStructuralSafe(pdfPath, ct), ct).ConfigureAwait(false);

        return new PdfPreflightReport(
            PageCount: structural.PageCount,
            PdfVersion: structural.PdfVersion,
            IsEncrypted: structural.IsEncrypted,
            FontCount: fonts.Count,
            NonEmbeddedFontCount: fonts.Count(f => !f.IsEmbedded),
            HasJavaScriptOrActions: scan.HasAnyJavaScriptOrActions,
            OutputIntentCount: intents.Count,
            HasIccOutputIntent: intents.Any(i => i.HasIccProfile),
            LinkCount: links.Count,
            HasExtractableText: structural.HasExtractableText);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Structural read is best-effort: any failure (corrupt/missing PDF, encrypted without password) yields default structural fields, not a throw; OperationCanceledException is rethrown.")]
    private StructuralInfo ReadStructuralSafe(string pdfPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var doc = PdfPigDocument.Open(pdfPath);

            // PdfPig Version — System.Double (verified by reflection, PdfPig 0.1.10). 0 ⇒ версия
            // неизвестна → пустая строка; иначе одна десятичная цифра, инвариантно культуре ("1.7").
            string version = doc.Version > 0d
                ? doc.Version.ToString("0.0", CultureInfo.InvariantCulture)
                : string.Empty;

            return new StructuralInfo(
                PageCount: doc.NumberOfPages,
                PdfVersion: version,
                IsEncrypted: doc.IsEncrypted,
                HasExtractableText: HasAnyExtractableText(doc, ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed structural preflight read of '{Path}'; returning defaults.", pdfPath);
            return StructuralInfo.Default;
        }
    }

    /// <summary>
    /// Есть ли непробельный извлекаемый текст хотя бы на одной из первых
    /// <see cref="TextScanPageCap"/> страниц. Короткое замыкание на первой непустой странице.
    /// </summary>
    private static bool HasAnyExtractableText(PdfPigDocument doc, CancellationToken ct)
    {
        int scanned = 0;
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                return true;
            }

            if (++scanned >= TextScanPageCap)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>Структурные поля отчёта, читаемые напрямую из PdfPig (vs. поля от под-инспекторов).</summary>
    private readonly record struct StructuralInfo(
        int PageCount,
        string PdfVersion,
        bool IsEncrypted,
        bool HasExtractableText)
    {
        /// <summary>Безопасные дефолты при провале структурного чтения.</summary>
        public static StructuralInfo Default => new(0, string.Empty, false, false);
    }
}
