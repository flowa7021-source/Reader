namespace Foliant.Domain;

/// <summary>
/// Сводный отчёт preflight'а / проверки структуры PDF: «годен ли документ для печати / архива /
/// пересылки?». Собирается read-only-сервисом, который <b>композирует</b> уже существующие
/// инспекторы PDF (шрифты, sanitization, output-intent'ы, ссылки) и добавляет структурную выжимку из
/// PdfPig (число страниц, версия формата, шифрование, наличие извлекаемого текста). В Acrobat это
/// аналог «Preflight» / «Document Properties» — но здесь только факты, без вердиктов.
///
/// <para>Pure-data, immutable, <b>без</b> локализованных строк: severity и человекочитаемые findings
/// («шрифт не встроен», «есть JavaScript», «нет ICC-профиля» и т.п.) выводит UI на основе этих
/// числовых / булевых полей. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов.</para>
/// </summary>
/// <param name="PageCount">Число страниц документа (PdfPig <c>NumberOfPages</c>). <c>0</c>, если
/// структурное чтение не удалось (битый / отсутствующий / зашифрованный без пароля PDF).</param>
/// <param name="PdfVersion">Версия формата PDF как строка с одной десятичной цифрой, например
/// <c>"1.7"</c> (инвариантно культуре). Пустая строка (<c>""</c>), если версия неизвестна или
/// структурное чтение не удалось.</param>
/// <param name="IsEncrypted"><see langword="true"/>, если документ зашифрован (выставлен флаг
/// шифрования PDF, ISO 32000-1 §7.6). <see langword="false"/> по умолчанию, если структурное чтение
/// не удалось.</param>
/// <param name="FontCount">Общее число уникальных шрифтов, используемых в документе (как их видит
/// <c>IPdfFontService</c>). <c>0</c>, если шрифтов нет или их чтение не удалось.</param>
/// <param name="NonEmbeddedFontCount">Сколько из <see cref="FontCount"/> шрифтов <b>не</b> встроены
/// (глифы отсутствуют в файле). Ненулевое значение — риск для печати / переносимости: при выводе на
/// другой системе шрифт может быть подменён. Всегда <c>&lt;=</c> <see cref="FontCount"/>.</param>
/// <param name="HasJavaScriptOrActions"><see langword="true"/>, если документ несёт document-level
/// JavaScript или автоматические действия каталога (<c>/OpenAction</c>-JS, <c>/Names → /JavaScript</c>,
/// catalog <c>/AA</c>; ISO 32000-1 §12.6 / §12.7) — потенциальный риск безопасности при открытии.</param>
/// <param name="OutputIntentCount">Число записей в массиве <c>/OutputIntents</c> каталога (ISO 32000-1
/// §14.11.5): деклараций целевого условия цветопередачи. <c>0</c>, если их нет.</param>
/// <param name="HasIccOutputIntent"><see langword="true"/>, если хотя бы один output-intent несёт
/// встроенный ICC-профиль (<c>/DestOutputProfile</c>) — документ готов к PDF/X / PDF/A в части
/// управления цветом.</param>
/// <param name="LinkCount">Число link-аннотаций (<c>/Subtype /Link</c>) во всём документе. <c>0</c>,
/// если ссылок нет.</param>
/// <param name="HasExtractableText"><see langword="true"/>, если хотя бы на одной (просканированной)
/// странице есть непробельный извлекаемый текст. <see langword="false"/> ⇒ документ, скорее всего,
/// image-only (скан) — кандидат на OCR.</param>
public sealed record PdfPreflightReport(
    int PageCount,
    string PdfVersion,
    bool IsEncrypted,
    int FontCount,
    int NonEmbeddedFontCount,
    bool HasJavaScriptOrActions,
    int OutputIntentCount,
    bool HasIccOutputIntent,
    int LinkCount,
    bool HasExtractableText);
