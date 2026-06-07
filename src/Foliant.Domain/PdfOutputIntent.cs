namespace Foliant.Domain;

/// <summary>
/// Одна запись из массива <c>/OutputIntents</c> каталога PDF (ISO 32000-1 §14.11.5, таблица 366):
/// декларация условия цветопередачи на выводе, под которое документ был подготовлен (PDF/X, PDF/A
/// и т.п.). В Acrobat это раздел «Document Properties → … / Output Preview / Preflight» (print
/// production / PDF-X readiness). Каждая запись — output-intent словарь (<c>/Type /OutputIntent</c>)
/// с обязательным подтипом <c>/S</c> и набором текстовых полей, описывающих целевое устройство /
/// характеристику печати; необязательный поток <c>/DestOutputProfile</c> несёт встроенный
/// ICC-профиль вывода.
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов. Только чтение — записи нет (зеркально fonts / links listing).</para>
/// </summary>
/// <param name="Subtype">Сырое имя подтипа <c>/S</c> без ведущего слэша (например <c>GTS_PDFX</c>,
/// <c>GTS_PDFA1</c>, <c>ISO_PDFE1</c>). Определяет, какому стандарту соответствует output intent.
/// Для записи без <c>/S</c> — пустая строка.</param>
/// <param name="OutputConditionIdentifier">Текстовая строка <c>/OutputConditionIdentifier</c>:
/// идентификатор условия вывода (как правило, имя из реестра ICC, например <c>FOGRA39</c> или
/// <c>CGATS TR 001</c>), или <see langword="null"/>, если ключ отсутствует.</param>
/// <param name="OutputCondition">Текстовая строка <c>/OutputCondition</c>: человекочитаемое описание
/// условия вывода для отображения пользователю, или <see langword="null"/>, если ключ отсутствует.</param>
/// <param name="RegistryName">Текстовая строка <c>/RegistryName</c>: имя реестра, в котором
/// зарегистрирован <see cref="OutputConditionIdentifier"/> (обычно URL, например
/// <c>http://www.color.org</c>), или <see langword="null"/>, если ключ отсутствует.</param>
/// <param name="Info">Текстовая строка <c>/Info</c>: дополнительная человекочитаемая информация об
/// условии вывода (обязательна, если <see cref="OutputConditionIdentifier"/> не из стандартного
/// реестра), или <see langword="null"/>, если ключ отсутствует.</param>
/// <param name="HasIccProfile"><see langword="true"/>, если запись содержит поток
/// <c>/DestOutputProfile</c> со встроенным ICC-профилем вывода. Присутствие потока — самодостаточный
/// признак; сами байты профиля read-API не извлекает.</param>
public sealed record PdfOutputIntent(
    string Subtype,
    string? OutputConditionIdentifier,
    string? OutputCondition,
    string? RegistryName,
    string? Info,
    bool HasIccProfile);
