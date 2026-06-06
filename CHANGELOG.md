# Changelog

Все заметные изменения проекта документируются здесь.

Формат: [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/).
Версии: [Semantic Versioning 2.0.0](https://semver.org/lang/ru/).

## [Unreleased]

### Added
- **Named destinations (`/Names/Dests` + legacy `/Dests`) — list / add / remove + UI**. Acrobat-фича
  «Destinations»: именованные точки перехода (имя → страница), на которые ссылаются ссылки/закладки.
  **Domain**: record `PdfNamedDestination(Name, PageIndex)`. **Application**: порт
  `IPdfNamedDestinationService` (`ListAsync` best-effort/sorted; `AddAsync` — добавить/заменить
  `[pageRef /Fit]`, pageIndex зажимается в `[0, pageCount-1]`; `RemoveAsync`). **Engine**:
  `PdfPigNamedDestinationService` + cos-helpers — `PdfNamedDestinationCosReader` читает **обе** формы
  (модерн name-tree `/Names/Dests` с рекурсией по `/Kids` + legacy catalog `/Dests`-словарь; резолвит
  page-ref → 0-based индекс через обход `/Pages`, как outline writer; модерн приоритетнее legacy при
  совпадении имён); `PdfNamedDestinationCosWriter` пишет в модерн `/Names/Dests` (inline-dest'ы),
  `PdfCatalogNamedDestinationsCosWriter` сохраняет прочие `/Names` sub-ключи (`/EmbeddedFiles`/`/JavaScript`)
  через `PdfIncrementalWriter`. MVP-ограничение: запись только в модерн-форму (имя из legacy `/Dests`
  читается, но `RemoveAsync` его не трогает) — документировано в порте. Оригинал не мутируется; Unicode
  имена round-trip. **DI + VM** `DocumentTabViewModel.NamedDestinations.cs` (load/add/remove). **UI**:
  `NamedDestinationsDialog` (список + форма имя/страница + Remove; возвращает одно действие) + меню
  **File → Named Destinations…**. L10n EN/RU. **29 engine + 11 VM-тестов** (вкл. legacy-`/Dests`-фикстуру).
- **Document fonts listing (Document Properties → Fonts) — read-only + UI**. Список шрифтов документа
  со статусом встраивания (важно для печати/PDF-A). **Domain**: record
  `PdfFontInfo(Name, Subtype, IsEmbedded)`. **Application**: порт `IPdfFontService.ListFontsAsync`
  (distinct, ordinal-sorted, best-effort). **Engine**: `PdfFontCosReader` обходит страницы
  (`/Pages` walk), читает `/Resources → /Font` каждой страницы, `/BaseFont` + `/Subtype`; embedded ⇔
  `/FontDescriptor` содержит `/FontFile|FontFile2|FontFile3` (для `/Type0` — через `/DescendantFonts[0]`);
  дедуп по (Name, Subtype, IsEmbedded). `PdfPigFontService` — read-only orchestrator. **UI**:
  `FontsDialog` (read-only список «имя — подтип — встроен/не встроен») + меню **File → Fonts…**.
  L10n EN/RU. **8 engine + 5 VM-тестов** (вкл. fixture с embedded и non-embedded шрифтом).
- **XMP metadata (`/Metadata`) — read + write + UI**. Просмотр и правка XMP-пакета документа
  (Acrobat «Additional Metadata»; XMP требуется для PDF/A и documents-of-record). **Application**: порт
  `IPdfXmpService` (`ReadAsync` → пакет как UTF-8 строка или `null` best-effort; `WriteAsync` —
  атомарная запись копии). **Engine**: `PdfPigXmpService` + cos-helpers (`PdfXmpCosReader` —
  catalog `/Metadata` поток, raw/FlateDecode; `PdfXmpCosWriter` — эмитит несжатый `/Metadata`-поток
  (`/Type /Metadata /Subtype /XML`, `/Length` = UTF-8 байты) через `PdfIncrementalWriter`,
  `PdfCatalogMetadataCosWriter` переписывает catalog). Оригинал не мутируется; Unicode (UTF-8)
  round-trip; reader снимает stream-level BOM. **DI + VM** `DocumentTabViewModel.Xmp.cs` (load/save).
  **UI**: `XmpMetadataDialog` (редактор XML-пакета со стартовым шаблоном) + меню **File → XMP
  Metadata…**. L10n EN/RU. **14 engine + 9 VM-тестов**.
- **Document JavaScript & actions sanitization (scan + remove) + UI**. Acrobat-фича «Sanitize»:
  обнаружение и удаление документ-уровневого JavaScript и автодействий — высокая ценность для
  юридического/нотариального сегмента. **Domain**: record `PdfSanitizationReport`
  (`DocumentJavaScriptNames`, `HasJavaScriptOpenAction`, `HasDocumentAdditionalActions`,
  `HasAnyJavaScriptOrActions`). **Application**: порт `IPdfSanitizationService` (`ScanAsync` →
  отчёт best-effort; `RemoveJavaScriptAndActionsAsync` → чистая копия, возвращает «удалено ли что-то»).
  **Engine**: `PdfPigSanitizationService` + cos-helpers — `PdfSanitizationCosReader` (catalog
  `/OpenAction` JS, `/Names → /JavaScript` name-tree, catalog `/AA`); `PdfSanitizationCosWriter` +
  `PdfCatalogSanitizationCosWriter` (убирают `/OpenAction` **только если это JS-действие** — GoTo
  сохраняется; дропают `/Names/JavaScript`, сохраняя прочие sub-ключи `/Dests`/`/EmbeddedFiles`;
  дропают catalog `/AA`) через `PdfIncrementalWriter`. Per-page/field `/AA` — вне scope (документировано).
  Оригинал не мутируется. **DI + VM** `DocumentTabViewModel.Sanitization.cs` (scan/remove). **UI**:
  `SanitizationDialog` (показывает находки + «Remove & Save As…») + меню **File → Remove JavaScript &
  Actions…**. L10n EN/RU. **15 engine + 9 VM-тестов** (фикстура с JS строится hand-rolled в тесте).
- **Embedded file attachments (`/EmbeddedFiles`) — list / extract / add / remove + UI**. Acrobat-фича
  «Attachments»: файлы, встроенные в PDF (катало­говое name-tree `/Names → /EmbeddedFiles`). **Domain**:
  record `PdfAttachment(Name, Size, Description)`. **Application**: порт `IPdfAttachmentService`
  (`ListAsync` best-effort/sorted; `ExtractAsync` — декодированные байты в файл, `KeyNotFoundException`
  если имени нет; `AddAsync` — встраивает файл, replace-on-collision; `RemoveAsync` — удаляет из
  name-tree). **Engine**: `PdfPigAttachmentService` + cos-helpers (`PdfAttachmentCosReader` —
  навигация name-tree с рекурсией по `/Kids`, декод `/EF /F` потока: raw либо FlateDecode через
  `System.IO.Compression.ZLibStream`; `PdfAttachmentCosWriter` — **первый stream-объект writer**:
  встраивает несжатый embedded-file поток + filespec + перестроенное name-tree через
  `PdfIncrementalWriter` (бинарные байты в тело объекта через byte-preserving Latin1);
  `PdfCatalogNamesCosWriter` — перезапись `/Names`, прочие sub-ключи (`/Dests`/`/JavaScript`)
  сохраняются). Оригинал не мутируется; бинарная точность (0x00..0xFF) проверена round-trip'ом;
  Unicode-имена/описания (UTF-16BE). **DI + VM** `DocumentTabViewModel.Attachments.cs` (gate / load /
  add / extract / remove). **UI**: `AttachmentsDialog` (список + Add/Extract/Remove, возвращает одно
  действие; View делает файловые диалоги) + меню **File → Attachments…**. L10n EN/RU. **33 engine + 13
  VM-тестов**.
- **Insert pages from another PDF (Acrobat «Insert Pages»)**. Вставка всех страниц другого PDF в
  указанную позицию. **Application**: порт `IPdfInsertPagesService.InsertAsync(source,
  insertAfterPageIndex, pdfToInsert, target, ct)` (0-based; `-1` = перед первой; out-of-range →
  `ArgumentOutOfRangeException`). **Engine**: `PdfPigInsertPagesService` через PdfPig `PdfMerger`
  (files `[source, insert, source]` + bundles `[[1..k+1],[1..ins],[k+2..n]]`; пустые head/tail-сегменты
  опускаются — empty-bundle копирует 0 страниц). Оригинал не мутируется; атомарная запись.
  **DI + VM** `DocumentTabViewModel.InsertPages.cs`. **UI**: `InsertPagesDialog` (позиция: «после
  страницы N», 0 = в начало) + меню **File → Insert Pages from PDF…**. L10n EN/RU. **17 engine + 9
  VM-тестов**.
- **Initial View / Viewer Preferences (`/PageLayout` · `/PageMode` · `/ViewerPreferences`) — read +
  write + UI**. Новая Acrobat-фича «Initial View»: как viewer открывает документ — раскладка страниц
  (single / continuous / two-page, нечётные слева/справа), активная навигационная панель (закладки /
  миниатюры / слои / вложения / full-screen) и пять UI-флагов (`HideToolbar` / `HideMenubar` /
  `FitWindow` / `CenterWindow` / `DisplayDocTitle`). **Domain**: enum'ы `PdfPageLayout` (7) и
  `PdfPageMode` (7) + immutable record `PdfViewerPreferences` со статикой `Default`. **Application**:
  порт `IPdfViewerPreferencesService` (`ReadAsync` best-effort → `Default`; `WriteAsync` атомарно).
  **Engine**: `PdfPigViewerPreferencesService` + cos-helpers (`PdfViewerPreferencesCosReader`
  навигирует catalog `/PageLayout`·`/PageMode`·`/ViewerPreferences`; `PdfViewerPreferencesCosWriter`
  инкрементально переписывает catalog через ту же инфру `PdfIncrementalWriter` /
  `PdfCatalogViewerPreferencesCosWriter`, что у `/PageLabels` и `/Outlines`). `Default` → ключи
  опускаются (PDF-default); false-флаги не пишутся; пустой `/ViewerPreferences` не создаётся; оригинал
  не мутируется. **DI**: регистрация + проброс в `DocumentTabViewModel`. **ViewModels**:
  `DocumentTabViewModel.ViewerPreferences.cs` — `CanEditViewerPreferences` gate, `CurrentViewerPreferences`
  снимок (`LoadViewerPreferencesCommand`), `SaveViewerPreferencesCommand`. **UI**: модальный
  `ViewerPreferencesDialog` (2 combo + 5 чекбоксов) + пункт меню **File → Initial View…**. Локализация
  EN/RU (26 ключей). **35 движковых + домен-тестов** (round-trip каждого layout/mode/флага, all-default
  drops keys, page-count preserved, source-not-mutated, arg-guards) **+ 11 VM-тестов** (gate / forward /
  load / no-op / suppress). Pure-managed PdfPig, зелёные на Linux.
- **Page labels — UI wiring (DI + «Number Pages» dialog + menu)**. Подключает
  `IPdfPageLabelService` / `PdfPigPageLabelService` (движок merged ранее) к приложению: регистрация в
  DI + проброс в `DocumentTabViewModel`. **ViewModels**: `DocumentTabViewModel.PageLabels.cs` —
  `CanEditPageLabels` gate (PDF + сервис), `CurrentPageLabels` снимок (`LoadPageLabelsCommand`),
  `SavePageLabelsCommand` с подавлением исключений (как соседние PDF-mutate команды). **UI**: модальный
  `PageLabelsDialog` (список диапазонов с add/remove + форма: начальная страница, стиль-combo, префикс,
  начальный номер; человекочитаемый sample через `PdfPageLabelFormatter`) + пункт меню
  **File → Number Pages…** (`IsEnabled` ← `CanEditPageLabels`) с SaveFileDialog. Локализация EN/RU
  (18 ключей). **11 VM-тестов** (`DocumentTabViewModelPageLabelsTests`): gate / forward ranges+path /
  load populates snapshot / load-throws-empty / non-PDF·null-service CanExecute=false / null-request ·
  blank-target no-op / service-throws-swallowed.
- **PDF page labels (`/PageLabels`) — read + write «Number Pages» (i, ii, iii → 1, 2, 3 → A-1 …)**.
  До этого PR номер страницы в навигаторе всегда был физическим индексом; стандартная Acrobat-фича
  «Number Pages» (именованные диапазоны нумерации `/PageLabels`, ISO 32000-1 §12.4.2) отсутствовала.
  Добавлены чистый domain + port + PdfPig-реализация, без UI/DI (wiring — отдельный PR, по образцу
  metadata #122 → #124). **Domain**: enum `PdfPageLabelStyle` (None / Arabic / UpperRoman / LowerRoman /
  UpperLetters / LowerLetters); record `PdfPageLabelRange` (`StartPageIndex` 0-based, `Style`, `Prefix?`,
  `Start` ≥ 1) с `Create`-фабрикой (валидация + нормализация: пустой префикс → `null`, `None` →
  `Start` = 1); `PdfPageLabelFormatter` (pure: набор диапазонов + индекс страницы → видимая метка;
  конвертеры римских/буквенных/арабских). **Application**: порт `IPdfPageLabelService` (`ReadAsync` —
  best-effort снимок, отсортированный по `StartPageIndex`; `WriteAsync` — атомарная запись копии).
  **Engine**: `PdfPigPageLabelService` + cos-helpers — `PdfPageLabelCosReader` навигирует
  `Catalog → /PageLabels → /Nums` (с рекурсией по `/Kids`), `PdfPageLabelCosWriter` строит number-tree
  и дописывает инкрементальным апдейтом через ту же инфру `PdfIncrementalWriter` /
  `PdfCatalogPageLabelCosWriter`, что у `/Outlines` (#123) и OCG (#119). Оригинал не мутируется
  (temp + Move); пустой список → валидный PDF **без** `/PageLabels`; дубликат стартового индекса →
  `ArgumentException` (ключи number-tree уникальны); Unicode-префиксы (кириллица) пишутся UTF-16BE+BOM
  hex и читаются обратно без потерь. **56 новых тестов**: 38 domain (валидация `Create` + formatter —
  римские I/IV/IX/XL/XC/MMXXIV, буквы A/Z/AA/ZZ/AAA, выбор диапазона слева, префиксы, edge-cases) +
  18 engine (round-trip arabic/roman/letters/prefix/Unicode/start-offset, unsorted → sorted output,
  empty drops `/PageLabels`, page-count preserved, source-not-mutated sha256, arg-guards) — pure-managed
  PdfPig, зелёные в Linux-CI.
- **Print (File → Print, Ctrl+P) — document-neutral via `IDocument.RenderPageAsync`**.
  Печать наконец появилась в ридере: пункт меню **File → Print…** и шорткат **Ctrl+P** показывают
  системный WPF `PrintDialog` (выбор принтера + диапазона), рендерят выбранные страницы через
  существующий `IDocument.RenderPageAsync` (BGRA32 → `BitmapSource`) и строят `FixedDocument`,
  который уходит в спулер через `PrintDialog.PrintDocument`. **Application**: новый порт
  `IPrintService` (DTO-less — диалог сам собирает выбор пользователя). **ViewModels**:
  `DocumentTabViewModel.Print.cs` partial с `PrintCommand` + `CanPrint` gate (сервис + страницы);
  опциональный ctor-параметр `printService`. **UI**: `WpfPrintService` (~250 строк) с маршалингом
  на UI-поток через `System.Windows.Application.Current.Dispatcher` (полная квалификация
  namespace — как в `WpfPasswordPrompt`); каждый `IPageRender` диспозится сразу после конвертации в
  `BitmapSource`, чтобы не держать буферы всех страниц одновременно. **DI**: регистрация
  `WpfPrintService` в `AppHostBuilder`; проброс в фабрику `DocumentTabViewModel`. Документ-neutral:
  работает для PDF, изображений, EPUB/FB2/MOBI, DjVu — потому что строится на абстракции
  `IDocument`, а не на PDF-специфике. Локализация EN/RU (2 ключа: `MenuFilePrint`, `PrintErrorMessage`).
  **11 новых VM-тестов** (`DocumentTabViewModelPrintTests`): gate (сервис/страницы/document-neutral),
  forwarding (документ + title из имени файла), no-op без сервиса/пустого документа, swallow
  exceptions (sync/async/OperationCanceled), составное имя файла как job-title. UI-сервис требует
  WPF runtime для тестов — валидируется compile-проверкой `EnableWindowsTargeting` (cross-platform
  + UI + App все 0 warnings).
- **Open password-protected PDFs (read-side decrypt) — prompt for password and retry**.
  Зашифрованные (password-protected) PDF теперь открываются: PDFium сам расшифровывает (AES/RC4)
  по переданному паролю — мы только проплываем пароль, ловим «нужен пароль» и спрашиваем у
  пользователя. **Domain**: новый типизированный `DocumentPasswordRequiredException`
  (`: InvalidOperationException`, с `Path` + фабрикой `ForPath`) + опциональный интерфейс
  `IPasswordAwareDocumentLoader` (существующий `IDocumentLoader` не тронут). **Engine**:
  `PdfDocumentLoader` реализует оба контракта; тело вынесено в core(`string? password`),
  `FPDF_LoadDocument(path, password)` вместо хардкод-`null`, а `FPDF_ERR_PASSWORD` (код 4)
  транслируется в типизированное исключение. **Application**: `OpenDocumentUseCase.ExecuteAsync`
  получил перегрузку `(path, password, ct)` (старая `(path, ct)` сохранена → reopen-вызов не
  тронут), пароль пробрасывается только в password-aware loader'ы; новый порт `IPasswordPrompt`.
  **ViewModels**: `MainViewModel` инжектит опциональный `IPasswordPrompt`; retry-loop
  (`TryLoadWithPasswordAsync`) спрашивает пароль с растущим `attempt`, тихо выходит на отмену,
  пробрасывает в headless-пути. **UI**: модальный `PasswordPromptDialog` (маскированный
  `PasswordBox`, Open/Cancel, retry-баннер) + `WpfPasswordPrompt` (маршалинг на UI-поток) + DI.
  Только read/view-only; шифрование при сохранении (write) остаётся `StubPdfEncryptionService`.
  Локализация EN/RU (4 ключа, общий `DialogCancel` переиспользован). **3 App + 4 VM + 4 Engine
  теста** (включая реальный RC4-фикстур, на котором PDFium возвращает «password required»).
- **Export Bookmarks to PDF — write sidecar bookmarks into the PDF /Outlines (wiring for #123, W7)**.
  Подключает `IPdfOutlineWriter`/`PdfPigOutlineWriter` к приложению: регистрация в DI
  (`AppHostBuilder`) + проброс в `DocumentTabViewModel` (опциональный ctor-параметр `outlineWriter`).
  Новый VM-partial `DocumentTabViewModel.OutlineExport.cs`: gate `CanExportBookmarksToPdf`
  (PDF-источник + writer подключён + непустой список закладок) и команда `ExportBookmarksToPdfCommand`,
  которая конвертирует `Bookmarks` → `IReadOnlyList<DocumentOutlineEntry>` (PageIndex/Label→Title/Depth,
  порядок сохранён) и делегирует в `WriteOutlineAsync` с подавлением исключений (как соседние
  PDF-mutate команды). **Nested сохранён** — `Bookmark.Depth` прокидывается, writer строит дерево
  из глубины, так что вложенные закладки переживают round-trip с «Import PDF Outline». Пункт меню
  **File → Export Bookmarks to PDF…** (`IsEnabled` ← `CanExportBookmarksToPdf`) с SaveFileDialog
  (PDF-filter, default-имя `<doc>-outline.pdf`). Локализация EN/RU (2 ключа). **11 новых VM-тестов**
  (`DocumentTabViewModelOutlineExportTests`): forward converted entries, preserve depth, gate
  (non-PDF / null-writer / empty-bookmarks), blank-target no-op, writer-throws-swallowed.
- **Document Properties dialog — edit PDF /Info metadata from the UI (wiring for #122)**.
  Подключает `IPdfMetadataEditService`/`PdfPigMetadataEditService` к приложению: регистрация в DI
  (`AppHostBuilder`) + проброс в `DocumentTabViewModel` (опциональный ctor-параметр
  `metadataEditService`). Новый WPF-диалог `DocumentPropertiesDialog` (по образцу
  `BatesNumberingDialog`) с editable-полями Title/Author/Subject/Keywords/Creator/Producer и
  read-only датами Created/Modified; пункт меню **File → Document Properties…**
  (`IsEnabled` ← `CanEditMetadata`, PDF-only). VM-команда `SaveMetadataCommand` делегирует в
  `EditAsync` с подавлением исключений (как соседние PDF-mutate команды);
  `CurrentMetadata`-снимок предзаполняет диалог. **Empty-field семантика**: пустой TextBox → `null`
  («не менять»), чтобы открытие диалога и Save без правок не стирали присутствующие/отсутствующие
  значения source. Локализация EN/RU (13 ключей). **10 новых VM-тестов**
  (`DocumentTabViewModelMetadataTests`): forward spec+path, non-PDF/null-service/null-request no-op,
  blank-target no-op, service-throws-swallowed, `CurrentMetadata`-snapshot.
- **PDF /Outlines writer — embed bookmarks back into PDF (symmetric to reader)**. Новый порт
  `IPdfOutlineWriter` (`Foliant.Application/Services`) + реализация `PdfPigOutlineWriter`
  (`Foliant.Engines.Pdf`): берёт плоский список `DocumentOutlineEntry` (PageIndex 0-based, Title,
  Depth 0-based) и встраивает его в PDF `/Outlines`, чтобы закладки стали видны в Acrobat / любом
  стороннем viewer'е — обратная операция к `PdfPigOutlineReader`. **Вариант B (cos
  incremental write)**: переиспользует инфру #111/#119 (`PdfIncrementalWriter`,
  `PdfDictionaryCosWriter`), эмитит N item-объектов + 1 root `/Outlines` dict + обновлённый catalog
  с `/Outlines N 0 R`; навигирует Catalog→/Pages→/Kids для `IndirectReference` каждой страницы
  (как `PdfPigAnnotationAppender`) и пишет `/Dest [pageRef /Fit]` на каждый узел. **Вложенность
  по Depth** через depth-stack: корректная linkage (`/First`/`/Last`/`/Next`/`/Prev`/`/Parent`) и
  `/Count`; Depth-«прыжки» зажимаются на 1 уровень/шаг, чтобы дерево оставалось связным.
  Оригинал не мутируется (инкрементальный апдейт + атомарная temp+Move запись в `targetPath`,
  source==target безопасно). Пустой список → валидный PDF без `/Outlines`. PageIndex вне диапазона
  страниц зажимается в `[0, pageCount-1]`. Unicode-заголовки (кириллица) пишутся UTF-16BE+BOM hex
  (паттерн `PdfAnnotationCosWriter`). НЕ покрыто намеренно: named destinations, open/closed
  состояние (`/Count` знак), цвета/стили (`/C`/`/F`), XYZ-zoom — только GoTo-page `/Fit`. cos-логика
  разнесена по helper'ам (`PdfOutlineCosWriter`, `OutlineLinks`, `PdfTextString`,
  `PdfCatalogOutlineCosWriter`) для соблюдения лимитов файла/метода. **12 новых тестов**
  (`PdfOutlineWriterTests`, чистый PdfPig, без Slow): round-trip против `PdfPigOutlineReader` —
  flat (3 entry) + nested (Depth 0/1/1/0) + Unicode lossless + empty + page-count сохраняется +
  source sha256 unchanged + PageIndex clamp; argument-контракт (blank source/target →
  `ArgumentException`, null entries → `ArgumentNullException`). Wiring (DI/UI) — отдельный PR.
- **PDF document metadata (/Info) editing — Title/Author/Subject/Keywords/Creator/Producer
  (PdfPig)**. До этого PR метаданные документа только **читались** (`IDocument.Metadata`);
  стандартная Acrobat-фича «Document Properties» (правка /Info) отсутствовала. Добавлены чистый
  port + PdfPig-реализация, без UI/DI (wiring — отдельный PR). Артефакты:
  `Foliant.Domain.PdfMetadataSpec` (record, все 6 полей `string?`, default `null`);
  `Foliant.Application.Services.IPdfMetadataEditService.EditAsync(sourcePath, targetPath, spec, ct)`;
  `Foliant.Engines.Pdf.PdfPigMetadataEditService`. Семантика поля: `null` → «не менять»
  (сохранить текущее значение source), пустая строка → «очистить» (PdfPig пишет пустой
  /Info-entry), непустая строка → перезаписать as-is (без культур-зависимостей). Реализация
  сливает source `.Information` со spec (`spec.X ?? source.X`) и ре-сериализует все страницы
  через `PdfMerger.Merge([source], [allPages], PdfAStandard.None, DocumentInformationBuilder)`;
  оригинал не мутируется (atomic temp + Move, паттерн split/extract). Контракт ошибок: blank
  пути → `ArgumentException`, null spec → `ArgumentNullException`, битый PDF/IO →
  пробрасывается. **Fidelity**: re-serialization сохраняет страницы, контент и аннотации —
  проверено тестом, Text- и Link-аннотации (вместе с их `/Contents`) переживают round-trip в
  PdfPig 0.1.10, потерь нет. Ограничение: XMP metadata stream и история incremental-update не
  сохраняются (заменяются свежим /Info); для documents-of-record с XMP — будущая Phase 3 через
  incremental `/Info` write. Scope не включает XMP, custom-properties, /CreationDate · /ModDate.
  **14 новых тестов** (pure-managed PdfPig, зелёные в Linux-CI): set-single-field,
  null-preserves-existing, empty-clears, all-six-round-trip, page-count-preserved (10→10),
  source-not-mutated (SHA-256), blank-path/null-spec arg-guards, annotation-fidelity,
  atomic-write-no-tmp-linger.
- **Q-F8 UI — OCG (PDF layers) panel + DI wiring**. Подключение F8-сервиса (`IPdfOcgService` /
  `PdfiumOcgService`, ранее зарегистрированного отдельным PR) к DI и UI: новая команда
  `ShowLayers` загружает снимок слоёв в `DocumentTabViewModel.CurrentLayers` через
  `IPdfOcgService.ReadLayersAsync`; команда `SaveLayerVisibility` делегирует в
  `SetLayerVisibilityAsync` с per-index dict пересечённых пользователем чекбоксов. UI: View →
  Layers… открывает модальный `LayersDialog` со списком `CheckBox` (two-way `IsVisible`),
  пустое состояние — текст «no layers»; Save Modified Copy… пишет атомарный новый PDF, оригинал
  не мутируется (паттерн watermark/redact). Новые типы: `PdfLayerViewModel` (mutable обёртка
  с `LayerName`/`Index`/`IsVisible` — имя свойства специально не `Name` против CS0108-конфликта
  с `FrameworkElement.Name`); `SaveLayerVisibilityRequest` record. Локали EN/RU синхронны:
  `MenuViewLayers`, `LayersDialogTitle`, `LayersEmpty`, `LayersSaveButton`, `LayersCancelButton`,
  `LayersSaveDialogTitle`. **13 новых VM-тестов**: gate-properties (3) + `ShowLayersCommand` (5,
  включая reload-replaces-snapshot + suppresses service throw) + `SaveLayerVisibilityCommand` (5,
  включая blank-target no-op + suppresses service throw) + `PdfLayerViewModel` (2). Self-check
  reserved-names + paramref пройден на обоих `.xaml.cs`.
- **Q-F-PdfA (port + honest stub) — PDF/A compliance validation interface
  (Вариант C)**. Greenfield-задача: до этого PR в Foliant не было ни порта, ни
  движка PDF/A-валидации. Реализован чистый port + «честная заглушка», чтобы
  зафиксировать контракт для будущей veraPDF-интеграции, не вводя в заблуждение
  callers (ничего не «валидируется молча», stub явно сообщает, что runtime не
  установлен). Артефакты: `Foliant.Domain.PdfAComplianceResult`
  (`Profile`/`IsCompliant`/`Issues`) и `PdfAValidationIssue`
  (`RuleId`/`Message`/`PageIndex?`) — value-records; enum
  `Foliant.Application.Services.PdfAProfile` с 8 профилями (`PdfA1B/1A`,
  `PdfA2B/2A/2U`, `PdfA3B/3A/3U` — нотация veraPDF, без PDF 2.0 / part 4 до
  поддержки нижележащим движком); port `IPdfAValidationService.ValidateAsync(
  sourcePath, profile, ct)`; реализация `StubPdfAValidationService` — проверяет
  arg-guard'ы (`ArgumentException`/`ArgumentOutOfRangeException`/
  `OperationCanceledException`) и бросает `NotSupportedException` с публичной
  константой `NotInstalledMessage`, содержащей подстроку «veraPDF» и ссылку на
  `https://verapdf.org`. **Почему Вариант C, а не A/B**: (A) единственный
  managed-биндинг `Codeuctivity.PdfAValidator` лицензирован под AGPL-3.0 —
  несовместимо с MIT Foliant (вирусно копилефтит всё приложение); (B) сам
  veraPDF (MPL-2.0/GPL-3.0+) — Java-tool, требует пакетирования JRE + jar и
  out-of-process-обвязки по образцу `plugins/Foliant.Plugin.DjVu` — отдельный
  PR. **Follow-up (отдельный PR)**: `plugins/Foliant.Plugin.VeraPdf` с
  `VeraPdfAValidationService` поверх `verapdf` CLI (`--format json`/`-f`),
  парсер JSON-репорта → `PdfAComplianceResult`, инсталлер скачивает jar +
  проверяет SHA256 (по образцу `tools/fetch-natives.ps1`). UI/DI/wiring сюда
  не входят — следующий PR. 4 теста на доменные records (форма/value-equality
  /null PageIndex для document-level issues) + 16 тестов на port/stub
  (наследование интерфейса, `NotSupportedException` со словом «veraPDF»,
  blank/null path → `ArgumentException`, неизвестный profile →
  `ArgumentOutOfRangeException`, pre-cancelled token → `OperationCanceledException`,
  все 8 профилей проходят arg-guard, публичная константа `NotInstalledMessage`).
- **Q-F30/F31 — port + домен PDF AES-256 шифрования (Phase 1 stub)**.
  Greenfield-точка расширения для шифрования PDF (Q-F30) и восьми permission-флагов
  (Q-F31), стратегически важная для юристов/корпоративного сектора. Новые типы:
  `PdfPermissions` (`[Flags]` enum в `Foliant.Domain`, биты 1..128 — `Print`, `Modify`,
  `Copy`, `Annotate`, `FillForms`, `Accessibility`, `Assemble`, `HighQualityPrint`,
  плюс `None`/`All`; биты НЕ совпадают с PDF P-entry — маппинг будет в реализации),
  `PdfEncryptionSpec` (immutable record c `Create`-фабрикой и инвариантами:
  user-password не null, owner-password не null/пуст, allowed permissions — flags),
  `IPdfEncryptionService` (port в `Foliant.Application/Services`: один метод
  `EncryptAsync(sourcePath, targetPath, spec, ct)`, контракт ошибок документирован —
  argument-валидация → атомарная запись через temp+Move). Реализация в этом PR —
  `StubPdfEncryptionService` в `Foliant.Engines.Pdf`: проверяет аргументы и бросает
  `NotSupportedException("Q-F30/F31, Phase 3 decision pending")`. Phase 1 — stub,
  потому что текущий PdfPig 0.1.10 не поддерживает запись encrypted output
  (`PdfDocumentBuilder` создаёт только открытые документы; чтение через
  `ParsingOptions.Password` — есть, запись — нет). Phase 3 решит между **QPDF embed**
  (production-quality, +~5 MB нативного бинаря в инсталлятор, cross-platform упаковка)
  и **raw cos-write через BouncyCastle** (managed-only, нулевой footprint, но
  ~600-800 строк encryption-dict + AES stream handler + R=6 password algorithm по
  ISO 32000-2 §7.6.4 + golden-тесты против Acrobat = 1-2 спринта). UI/DI wiring —
  отдельный PR (port готов к регистрации в `AppHostBuilder` и пробросу в VM-фабрику
  без правки domain/Application). **31 новый тест**: 9 параметризованных bit-value
  тестов `PdfPermissions` (закрепляют 1/2/4/.../128 + `All=0xFF` + `[Flags]`-атрибут),
  6 record-семантика+валидация `PdfEncryptionSpec` (round-trip, value-equality,
  `with`-копии, null-user / null-or-empty-owner → `ArgumentException`),
  10 contract-тестов `StubPdfEncryptionService` (NotSupported с маркером Q-F30/F31,
  blank source/target → ArgumentException, null spec → ArgumentNullException,
  pre-cancelled token → OperationCanceledException, реализация — `IPdfEncryptionService`).
- **Q-F8 — OCG (Optional Content Groups, «PDF layers») чтение + переключение
  видимости (Phase 2 MVP)**. Новый domain-record `Foliant.Domain.PdfLayer`
  (`Index`, `Name`, `IsVisible`), порт
  `Foliant.Application.Services.IPdfOcgService` с двумя методами
  (`ReadLayersAsync` — снимок текущих слоёв; `SetLayerVisibilityAsync` —
  atomic temp+Move запись в новый файл с обновлённой default-visibility,
  оригинал не мутируется). Реализация `Foliant.Engines.Pdf.PdfiumOcgService` —
  pure-managed: PDFiumCore 146.x не экспонирует OCG-bindings (проверено через
  `strings PDFiumCore.dll | grep -i ocg`), поэтому работаем целиком через
  PdfPig + cos-write. Чтение в `PdfOcgCosReader`: навигация `Catalog →
  /OCProperties → /OCGs`-массив индирект'ов, парс `/Name` каждого OCG и
  вычисление default-visibility из `/D → /OFF/ /ON/ /BaseState` по PDF spec
  §8.11.4.4. Запись в `PdfOcgCosWriter` + `PdfCatalogCosWriter`: перевыписка
  `/D`-словаря с новыми `/ON`/`/OFF` массивами поверх `PdfIncrementalWriter`
  (тот же механизм инкрементального апдейта, что в PR #111 для Line/Polygon
  аннотаций). Невалидные индексы в `visibilityByIndex` игнорируются
  silently. **НЕ в scope MVP** (отложено на Phase 2+/3): создание/удаление
  слоёв, перемещение объектов между слоями, иерархия (parent/child через
  `/Order`), OCMD (Optional Content Membership Dictionary), сложные
  visibility-правила (`/VE`), `/RBGroups`, `/Locked`, `/AS`-overrides. UI/DI
  wiring — отдельный follow-up PR (W-серия). 9 unit-тестов
  (`PdfiumOcgServiceTests`, `[Trait("Category", "Slow")]`): чтение 3 слоёв с
  именами и default-visibility, пустой результат для PDF без OCG, toggle
  слоя 0, idempotent empty-dictionary apply, out-of-range index ignored,
  оригинал не мутируется (sha256-инвариант), re-enable previously hidden,
  null-/blank-аргумент кейсы.
- **Q-F12 (UI wiring) — Split-into-files + Extract-pages меню + диалоги**.
  `IPdfSplitService` уже зарегистрирован в `AppHostBuilder` и проброшен в
  `DocumentTabViewModel` factory (PR #105) — здесь добавлена только UI-обвязка.
  В `DocumentTabViewModel.SplitEffects.cs`: гейт `CanSplitPdf` (true ↔ PDF +
  сервис зарегистрирован), `SplitEveryCommand(SplitEveryRequest)` поверх
  `IPdfSplitService.SplitEveryAsync(source, pagesPerChunk, outputDir, baseName, ct)`
  и `ExtractSelectionCommand(ExtractSelectionRequest)` поверх
  `ExtractSelectionAsync(source, pageIndices0Based, target, ct)`; оба
  suppress-ят исключения как соседние команды. Новые WPF-диалоги (по образцу
  `CropDialog`): `SplitEveryDialog` (numeric «pages per file» ≥ 1, свойство
  `SplitChunkSize`) и `ExtractSelectionDialog` (1-based range-строка вида
  «1-3,7,10-12» → ordered/de-duplicated 0-based индексы, валидация против
  page count, свойство `SelectionText`). File-меню (после Merge): «Split into
  Files…» (`MenuFileSplitEvery`) + «Extract Pages…» (`MenuFileExtractSelection`),
  оба `IsEnabled` на `SelectedTab.CanSplitPdf`, обработчики
  `OnSplitEveryMenuItemClick` (диалог → OpenFolderDialog → команда) и
  `OnExtractSelectionMenuItemClick` (диалог → SaveFileDialog → команда). Strings:
  13 новых ключей в `Strings.resx` / `Strings.ru.resx` (`MenuFileSplitEvery`,
  `MenuFileExtractSelection`, `SplitEveryDialogTitle`, `SplitPagesPerFileLabel`,
  `SplitOkButton`, `SplitCancelButton`, `SplitFolderDialogTitle`,
  `ExtractSelectionDialogTitle`, `ExtractPagesLabel`, `ExtractPagesHint`,
  `ExtractOkButton`, `ExtractCancelButton`, `ExtractSaveDialogTitle`). +13
  VM-тестов (forwarding обеих команд, CanExecute-гейты non-PDF / service-null,
  null-/empty-request no-op, service-throws не пробрасывается).
- **Q-F (UI wiring) — Bates-нумерация меню + диалог**. `IBatesNumberingService`
  уже зарегистрирован в `AppHostBuilder` и проброшен в `DocumentTabViewModel`
  factory (PR #106) — здесь добавлена только UI-обвязка. Команда VM в
  `DocumentTabViewModel.PdfEffects.cs`: `CanApplyBates` (true ↔ PDF +
  `IBatesNumberingService` зарегистрирован) и
  `ApplyBatesCommand(ApplyBatesRequest)` поверх
  `IBatesNumberingService.ApplyAsync(source, spec, target, ct)`. Новый
  WPF-диалог `BatesNumberingDialog` (по образцу `HeaderFooterDialog`): TextBox
  для Prefix/Suffix, валидируемые поля Start number / Digits, слайдеры Font
  size + R/G/B, ComboBox позиции (BottomLeft/Center/Right), TextBox Page range
  (пусто = все страницы; счётчик при этом не сдвигается) + OK/Cancel. Меню File
  → «Bates Numbering…» (`MenuFileBatesNumbering`, после Header/Footer) с
  обработчиком `OnApplyBatesMenuItemClick`: SaveFileDialog → диалог →
  `ApplyBatesCommand.ExecuteAsync`. Strings: 17 новых ключей в `Strings.resx` /
  `Strings.ru.resx` (`MenuFileBatesNumbering`, `BatesDialogTitle`,
  `BatesPrefixLabel`, `BatesSuffixLabel`, `BatesStartNumberLabel`,
  `BatesDigitsLabel`, `BatesFontSizeLabel`, `BatesPositionLabel`,
  `BatesColorLabel`, `BatesPageRangeLabel`, `BatesPositionBottomLeft/Center/Right`,
  `BatesOkButton`, `BatesCancelButton`, `BatesSaveDialogTitle`). 8 VM-тестов в
  `DocumentTabViewModelPdfEffectsTests.cs` (форвардинг spec+path, CanExecute
  gate'ы для non-PDF / отсутствующего сервиса, guard'ы null-request /
  blank-target, suppression исключений сервиса).
- **Q-F32 (UI wiring) — Find-and-Redact меню + диалог**. `IFindAndRedactService`
  зарегистрирован в `AppHostBuilder` (singleton) и пробрасывается в
  `DocumentTabViewModel` factory. Расширены команды VM в
  `DocumentTabViewModel.PdfEffects.cs`: `CanRedactPages` (true ↔ PDF + хотя бы
  один из `IRedactionService`/`IFindAndRedactService` зарегистрирован),
  `RedactPagesCommand(RedactPagesRequest)` для координатных регионов (batch /
  программные сценарии) и `FindAndRedactCommand(FindAndRedactRequest)` поверх
  `IFindAndRedactService.RedactMatchesAsync`. Новый WPF-диалог `RedactionDialog`
  (по образцу `CropDialog`): TextBox для query + чекбоксы «Match case» / «Whole
  word» / «Regex» / «Fold diacritics» / OK/Cancel. Меню File → «Find and
  Redact...» (`MenuFileRedact`) с обработчиком `OnRedactPagesMenuItemClick`,
  открывающим SaveFileDialog → диалог → `FindAndRedactCommand.ExecuteAsync`.
  Strings: 9 новых ключей в `Strings.resx` / `Strings.ru.resx`
  (`MenuFileRedact`, `RedactionDialogTitle`, `RedactionQueryLabel`,
  `RedactionMatchCase`, `RedactionWholeWord`, `RedactionRegex`,
  `RedactionFoldDiacritics`, `RedactionOkButton`, `RedactionCancelButton`,
  `RedactionSaveDialogTitle`). **12 новых VM-тестов** в
  `DocumentTabViewModelPdfEffectsTests` (CanRedactPages-гейты, forward в оба
  сервиса, null/blank/empty-region guard'ы, non-PDF / service-not-registered
  → CanExecute=false, исключения сервиса не пропагируются). MVP: визуальный
  drawing регионов мышью отложен в Wave 5+ (`RedactPagesCommand` уже принимает
  готовые `RedactionRegion`'ы — батч / плагины подключаются без UI).
- **Q-F32 (partial follow-up) — find-and-redact wrapper**. Поверх координатного
  `IRedactionService` (PR #102) добавлен `IFindAndRedactService` /
  `FindAndRedactService`: принимает документ, путь источника/цели, query (текст или
  .NET Regex) и `FindAndRedactOptions` (`CaseSensitive`, `WholeWord`, `Regex`,
  `FoldDiacritics`). Substring-путь делегирует `ISearchService` (реюз whole-word /
  fold-diacritics / case-sensitivity); regex-путь читает text layer напрямую и
  применяет .NET Regex с `RegexOptions.IgnoreCase` при `CaseSensitive=false` и
  2-секундным timeout. Каждое совпадение → `RedactionRegion(pageIndex, bbox)` →
  `IRedactionService.RedactAsync`. Нулевые матчи → output не пишется, возврат 0.
  Bbox = геометрия `TextRun`'а, в который попало начало совпадения (PDF text layer
  пер-строчный — sub-character координат нет). Параллельно расширен `SearchHit`
  опциональным полем `Bbox`: `SearchService.SearchInDocumentAsync` теперь
  populates его per-hit на основе per-run start-offsets (бинарный поиск по
  отсортированному массиву начал). Поле необязательное (`AnnotationRect?`, default
  `null`) — позиционные конструкторы `new SearchHit("", "/p", 0, "snippet", 1.0)`
  в sidebar / SqliteFtsIndex / ViewModels-тестах продолжают работать. **16 тестов**:
  4 на bbox-population в `SearchServiceTests`, 12 на `FindAndRedactServiceTests`
  (substring/regex/whole-word/case-sensitivity/0-matches/argument-validation/
  cancellation/multi-page). DI-wiring (`AppHostBuilder`) и Find-And-Redact UI —
  follow-up.
- **Q-F17 (11/11) — native PDF embedding для Line/Arrow/Polygon через cos-level fallback**.
  PDFium 146.x не экспонирует setter'ы для `/L` / `/Vertices` / `/LE`, поэтому
  `AnnotatedPdfExportService` после `FPDF_SaveAsCopy` гонит байты через новый
  `PdfPigAnnotationAppender`: PdfPig читает page-tree, новый `PdfIncrementalWriter` дописывает
  инкрементальный апдейт (ISO 32000-1 §7.5.6) — новые annotation-объекты (`/Type /Annot
  /Subtype /Line|/Polygon`) + обновлённые page-словари с расширенным `/Annots` массивом + новая
  xref/trailer со `/Prev` на старую. Cos-сериализация: `PdfAnnotationCosWriter` (Line/Arrow:
  `/L [x1 y1 x2 y2]`, Arrow добавляет `/LE [/None /OpenArrow]`; Polygon: `/Vertices [x1 y1 ...]`)
  + `PdfDictionaryCosWriter` (rewrite page-словаря с union существующих и новых `/Annots`).
  Unicode `/T`/`/Subj` сериализуется hex-string в UTF-16BE с BOM. Остальные 8 типов
  (highlight/underline/strikeout/note/ink/square/circle/stamp) продолжают embedд'иться через
  PDFium как раньше — этот PR закрывает 11/11 типов в native /Annots. **7 тестов** в
  `AnnotatedPdfExportServiceLineArrowPolygonTests` (Slow): Line с точными координатами, Arrow с
  `/LE [/None /OpenArrow]`, Polygon с `/Vertices`, смешанный набор на одной странице (line +
  highlight + note, все 3 видны PDFium'у), много страниц, Unicode metadata, валидность вывода
  для PDFium и PdfPig.
- **Husky.Net pre-push hook — автоматизация §3 «Пред-пуш чеклист» из `docs/DEV_RETROSPECTIVE.md`**.
  Локальный dotnet-tool `Husky` (`.config/dotnet-tools.json` + `Directory.Build.targets` с
  `AfterTargets="Restore"` MSBuild-таргетом → авто-`dotnet husky install` при первом
  `dotnet build`/`dotnet test` после клона, инкрементально через `.husky/_/install.stamp`;
  голый `dotnet restore` не триггерит — это известное ограничение SDK, на практике build/test
  идут сразу после клона). Hook `.husky/pre-push` запускает группу `pre-push` из
  `.husky/task-runner.json`: (1) `dotnet format whitespace --verify-no-changes`,
  (2) `dotnet format style --verify-no-changes --severity warn`, (3) `dotnet build
  Foliant.CrossPlatform.slnf -c Release -f net10.0 -warnaserror -maxcpucount:1`,
  (4) `dotnet test … --filter "Category!=Slow&Category!=Integration&Category!=E2E"` (~1100
  cross-platform unit-тестов, ~60s end-to-end). Slow/Integration/E2E и Windows-only PDFium-тесты
  вынесены за hook (живут в `verify.yml` / `tools/verify-local.sh`). Bypass: `HUSKY=0`,
  `SKIP_HOOKS=1`, либо `git push --no-verify`. Кросс-платформа (Linux/Windows/Mac) без
  Node.js — Husky.Net это pure-.NET tool. Все CI-workflows получили `HUSKY=0` в env (auto-install
  на CI бесполезен). Установка для новых contributor'ов задокументирована в `CONTRIBUTING.md` +
  `docs/BUILD.md`.
- **Q-F32 (partial) — физический redaction PDF (координатный MVP)**. Новый порт
  `IRedactionService` + PDFium-реализация `PdfiumRedactionService`: на вход путь к PDF и список
  областей (`RedactionRegion` = страница + `AnnotationRect` в PDF user space). Для каждой области
  текстовые page-object'ы, чьи bbox пересекают прямоугольник, **физически удаляются** из контента
  и текстового слоя (`FPDFPageRemoveObject` + `FPDFPageObjDestroy`), затем поверх рисуется
  непрозрачный чёрный бокс (`FPDFPageObjCreateNewRect` + fill). Результат пишется атомарно в новый
  файл — оригинал не мутируется (паттерн watermark/header-footer: NativeGate, GCHandle-pinning,
  temp+Move). Find-and-redact по тексту/regex, удаление изображений, метаданные/OCG и DI/UI-wiring —
  follow-up. Тесты: `PdfiumRedactionServiceTests` (6 integration-тестов на реальном PDFium —
  слово под областью исчезает из текстового слоя, текст вне области сохраняется, пустой список →
  валидный PDF, невалидный индекс страницы → guard, выход переоткрывается как валидный PDF).
- **Q-F26 (partial) — PAdES-B криптовалидация подписей PDF**. `PdfSignatureController.ValidateAsync`
  больше не заглушка: делегирует в новый `PadesValidator` (pure, без PDFium). Валидируется
  B-level: (1) CMS/PKCS#7 подпись против дайджеста подписанных байт (`SignedCms.CheckSignature`
  detached); (2) целостность документа — `/ByteRange` покрывает весь файл кроме окна `/Contents`
  (incremental-update после подписи → `DocumentUntouchedSinceSigning=false`); (3) цепочка
  сертификата до доверенного корня (`X509Chain`, с опциональным custom trust-anchor) + срок
  действия. `/ByteRange`+`/Contents` парсятся напрямую из сырых байт (`ByteRangeParser`, точные
  офсеты + DER-trim zero-padding). T-level (TSA timestamp) / LT / LTA / revocation (CRL/OCSP) —
  **не** проверяются на этом уровне (Phase 2 follow-up). **7 hermetic-тестов** в
  `PadesValidatorTests` (self-signed cert + подписанный ByteRange генерируются в самом тесте).
- **PDF split + cherry-pick экспорт (`IPdfSplitService` / `PdfPigSplitService`, PdfPig)**. Дополняет
  одно-диапазонный `IPageRangeExtractor` двумя частыми операциями: `SplitEveryAsync` режет документ
  на файлы по N страниц (`{base}-001.pdf`, `{base}-002.pdf`…, последний — остаток; нумерация
  инвариантна культуре), `ExtractSelectionAsync` собирает один PDF из произвольной не-непрерывной
  выборки страниц строго в указанном порядке. 0-based индексы, атомарная запись (tmp + Move),
  невалидные/out-of-range → `ArgumentOutOfRangeException`, пустая выборка → `ArgumentException`.
  **11 тестов** в `PdfPigSplitServiceTests` (pure-managed, без PDFium). DI-проводка — follow-up.
- **Bates numbering — последовательные юридические штампы страниц (паритет с Acrobat Pro)**.
  Новый `BatesNumberingSpec` (Domain: `Prefix`/`Suffix`/`StartNumber`/`Digits` zero-pad/`BatesPosition`
  нижний угол/`FontSize`/RGB/опциональный `PageRange`), порт `IBatesNumberingService` и реализация
  `PdfiumBatesNumberingService` (PDFium text-object в нижнем углу, монотонный счётчик по абсолютному
  индексу страницы → номера стабильны при печати поддиапазона, atomic save в новый файл — оригинал не
  мутируется). В отличие от header/footer: нет произвольного текста/placeholder'ов, только
  `{Prefix}{номер:D{Digits}}{Suffix}`. **13 тестов** (`PdfiumBatesNumberingServiceTests`, `Slow`).
  DI-wiring — follow-up.
- **A5c — image-stamp `ImagePath` round-trip через XFDF/FDF (закрытие угловых случаев)**. XFDF
  пишет custom `foliant:imagepath` атрибут в собственном namespace, FDF — custom `/FoliantImagePath`
  ключ в annotation-словаре (PR #95). Этот follow-up добивает acceptance-чеклист: (a) Stamp с
  ImagePath сериализуется и парсится буква-в-букву (уже было); (b) текстовый Stamp → ImagePath
  == null (уже было); (c) ни один не-Stamp тип не несёт ImagePath после round-trip; (d) Unicode
  (кириллица + non-BMP code points) и XML/PDF-метасимволы (`&`, `<`, `>`, кавычки, `(`/`)`/`\`)
  в пути проходят без потерь. Ещё **8 тестов** в `XfdfImagePathRoundTripTests` /
  `FdfImagePathRoundTripTests` (Application total: 372).

### Changed
- **DI-проводка `IRedactionService` / `IPdfSplitService` / `IBatesNumberingService`** в
  `AppHostBuilder`: 3 singleton-регистрации (`PdfiumRedactionService` / `PdfPigSplitService` /
  `PdfiumBatesNumberingService`) рядом с уже существующими PDF-сервисами + проброс в
  `DocumentTabViewModel`-factory как опциональные параметры (`= null`, через `sp.GetService<…>()`).
  UI-обвязка (команды VM, пункты меню, диалоги, ресурсные строки) — следующая серия PR (W1/W2/W3).

## [0.1.0] - 2026-05-26

Первый альфа-релиз Foliant. Функционально покрывает объём Phase 1 (просмотр
PDF/DjVu, поиск, аннотации, OCR, локализация, лицензия/триал, инсталлятор), но
ещё не прошёл широкую проверку на разных документах и машинах. Возможны
шероховатости. Известные ограничения см. в `.github/release-notes/v0.1.0.md`.

### Changed
- **OCR-движок: Tesseract → PaddleOCR (in-process через Sdcb.PaddleOCR)**. Реализован `PaddleOcrEngine : IOcrEngine` в `Foliant.Engines.Ocr` (растр BGRA32 → OpenCvSharp `Mat` → PaddleOCR; каждая распознанная строка → `TextRun` с bounding-box в пикселях рендера; `SemaphoreSlim`-сериализация, т.к. `PaddleOcrAll` не потокобезопасен; модели оффлайн из `native/paddleocr/`). Чистый маппинг языков `OcrLanguageMap` (Tesseract-стиль `"eng+rus"` → набор моделей latin/cyrillic). Порты `IOcrEngine`/`OcrPageUseCase`/`OcrPipelineService`/`OcrDiskCache` не менялись. Весь OCR-конвейер дорегистрирован в DI (`AppHostBuilder`). Пакеты: убран `Tesseract`, добавлены `Sdcb.PaddleOCR`/`Sdcb.PaddleInference`/`Sdcb.PaddleInference.runtime.win64.mkl`/`OpenCvSharp4`(+runtime.win). `tools/fetch-natives.ps1` качает модели PaddleOCR по скриптам/tier'ам. Обновлены `PROJECT_BOARD.md`, `IMPLEMENTATION_PLAN.md` (S8, §5.3), `README.md`, `NOTICE.md`. Тесты: `OcrLanguageMapTests` + guard-тесты `PaddleOcrEngineTests`. Замечание: версии пакетов/имена моделей и сборку проверить на Windows-стенде (нет .NET SDK в текущем окружении).

### Fixed
- **Корень: цикл сборки.** Не-WPF проекты переведены на `net10.0` (Infrastructure/DjVu — мульти-таргет с `#if WINDOWS`); весь логический слой собирается и юнит-тестируется на Linux (`tools/verify-local.sh`, ~807 тестов) и в CI. `verify.yml`: Linux-джоб + Windows build+test.
- `FileFingerprint` — хэш фиксированного 64KB-окна (ArrayPool мог вернуть больший буфер → недетерминированный fingerprint для файлов > 64KB).
- `MemoryPageCache` sticky — реальные ключи окна (а не угаданные), центр — самым свежим.
- `JsonSearchHistoryService` — сериализация фоновых сохранений (гонка на `.tmp`).
- `SqliteFtsIndex`/`SqliteDiskCache` — потокобезопасная инициализация / запись под gate (stale-hit после invalidate).
- `PdfDocument` — off-by-one извлечения текста, OOB-чтение метаданных, краш `ParsePdfDate` на битой дате.
- `PdfDocumentEditor` Undo/Redo — exception-safe (мутация состояния только после успешных await).
- VM-рендер: generation-guard (устаревший рендер не перетирает текущий); подсветка поиска — тоже guard + whole-word.
- CI: гонка параллельной сборки solution с `-f` (`-maxcpucount:1`); де-флейк трёх progress-тестов (`Progress<T>` → синхронный `IProgress`).

### Added
- **Режимы просмотра**: Single / Continuous (виртуализованная лента) / Two-Page + Fit Width/Page/Actual; `RenderedPageViewModel` (ленивый per-page рендер).
- **Полоса миниатюр** (`ThumbnailStripViewModel`): ленивые миниатюры, клик-навигация, drag-drop reorder.
- **On-page подсветка поиска** (`SearchHighlight`) и оверлей аннотаций во всех режимах просмотра.
- **PDF→DOCX** (`DocxDocumentExportService`, OpenXml); **DjVu**-плагин (out-of-process, MEF).
- **Аннотации**: `AnnotationLayer` (overlay, hit-test, заметка по двойному клику) + сайдбар «All Annotations» с JSON-экспортом.
- **Управление страницами**: rotate/delete/move/reorder через `PdfPageEditService` + reload.
- **Лицензия**: DPAPI-хранилище (`DpapiLicenseStorage`/`TrialStores`) + диалог импорта (Tools → Import License, ECDSA-P256 dev-ключ).
- Корректная палитра Dark/HighContrast (`RenderColorMap` в Domain, общая для PDFium и DjVu).
- `PROJECT_BOARD.md` — концепт проекта, 68 закрытых решений, фазы, риски.
- `IMPLEMENTATION_PLAN.md` — детальный план реализации Phase 0/1, контракт качества кода, контракты Domain.
- Структура репозитория (`src/`, `tests/`, `plugins/`, `installer/`, `tools/`, `docs/`, `.github/`).
- Базовая конфигурация: `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`.
- Метаданные: `README.md`, `LICENSE` (MIT), `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `NOTICE.md`.
- CI/CD пайплайны: `ci.yml`, `codeql.yml`, `release.yml`, `perf.yml`.
- Скелет solution с 9 проектами + тестовыми проектами.
- `Foliant.Domain` — базовые контракты: `IDocument`, `IPageRender`, `RenderOptions`, `TextLayer`, `DocumentMetadata`.
- Composition root в `Foliant.App` со Serilog + DI.
- `Foliant.Infrastructure.Storage.FileFingerprint` — sha256(first 64 KB ‖ size ‖ mtime) для ключей кэша.
- `Foliant.Infrastructure.Caching.LruCache<TKey, TValue>` — потокобезопасный LRU с capacity-by-bytes и автоматическим Dispose эвиктируемых значений.
- `Foliant.Infrastructure.Settings`: `AppSettings` (schema-versioned record), `JsonSettingsStore` (атомарная запись через .tmp + System.Text.Json source-gen + миграции), `SettingsMigrator`.
- `Foliant.Application.UseCases.OpenDocumentUseCase` — маршрутизатор открытия документа по `IDocumentLoader[]`.
- `Foliant.Engines.Pdf.PdfDocumentLoader` — детект PDF по расширению или магии `%PDF-`. `LoadAsync` — заглушка до S1.
- DI-регистрации в `Foliant.App.Composition.HostBuilder`.
- Ещё **31 unit-тест**: FileFingerprint (5), LruCache (10), JsonSettingsStore (6), OpenDocumentUseCase (5+), PdfDocumentLoader (8).
- `Foliant.Infrastructure.Caching.MemoryPageCache` — слой 1 кэша рендера: `LruCache<CacheKey, IPageRender>` с capacity-by-bytes (Stride×Height) и sticky-окном ±N от текущей страницы.
- `Foliant.Infrastructure.Caching.IDiskCache` + `SqliteDiskCache` — слой 4 (persistent): файлы в `pages/`, метаданные в SQLite (WAL), атомарная запись через .tmp + Move(overwrite), LRU-эвикция, инвалидация по document fingerprint, выживает рестарт процесса. Concurrent-safe для разных ключей.
- DI-регистрация cache-сервисов в `HostBuilder` (RAM: min(15 % памяти системы, 1 ГБ, ≥ 128 МБ); Disk: `AppPaths.Cache`).
- Ещё **17 тестов**: MemoryPageCache (6 unit), SqliteDiskCache (11 integration: roundtrip, eviction, restart-survival, concurrent Put, …).
- `Foliant.Domain.SearchHit` + `SearchQuery` records.
- `Foliant.Infrastructure.Search.IFtsIndex` + `SqliteFtsIndex` — слой 5: FTS5 поверх `documents` + `pages_fts` (unicode61 + remove_diacritics), bm25 ранжирование, `snippet(...)` для подсветки, инвалидация по document fingerprint, ограничение по документу.
- `Foliant.Infrastructure.Caching.CacheJanitor` — `BackgroundService` с `PeriodicTimer`, держит DiskCache ниже soft-limit (90 % hard), отказоустойчивый (исключения логируются, не пробрасываются).
- DI: `IFtsIndex` (на `AppPaths.Cache/index/fts.db`), `CacheJanitorOptions`, `AddHostedService<CacheJanitor>`.
- `docs/ARCHITECTURE.md` — карта слоёв, правила зависимостей, threading-карта, ссылки на под-документы.
- `docs/CACHE.md` — детальное описание 5 слоёв, ключа, инвалидации, метрик.
- `docs/PLUGINS.md` — две модели плагинов (in-process MEF / out-of-process Process.Start), карта Pro и опц. плагинов.
- `docs/BUILD.md` — инструкции сборки, performance, кросс-платформенные нюансы, troubleshooting.
- Ещё **13 тестов**: SqliteFtsIndex (10 integration: roundtrip, RestrictToDoc, MaxResults, reindex replaces, remove, list ordered desc, diacritics-insensitive), CacheJanitor (3 unit).
- **S5 (A) — Recent Files**: `IRecentsService` + `RecentsService` (MRU, кэп=20, case-insensitive dedup, персист через `ISettingsStore`, concurrent-safe). `MainViewModel.RecentFiles` + `OpenRecentCommand` + `ClearRecentsCommand`. Подменю `File → Open Recent`. FileNotFoundException при открытии → авто-удаление из MRU.
- **S5 (B) — SettingsWindow**: `AppSettings` / `ISettingsStore` перемещены в `Foliant.Application.Settings` (правильный слой). `ISettingsService` + `SettingsService` (кэш + concurrent-safe сохранение). `SettingsViewModel` (тема, язык, размер дискового кэша, очистка при выходе). `SettingsWindow.xaml` — модальный диалог OK/Cancel. `MainWindow: Tools → Settings...`. `InitializeAsync` загружает настройки и применяет тему из файла.
- **S5 (C) — Локализация RU/EN с hot-switch**: `ILocalizationService` (Application port) + `LocalizationManager` (singleton в `Foliant.UI`, `INotifyPropertyChanged`, рейзит «Item[]» при смене культуры). `Resources/Strings.resx` (en, default) + `Strings.ru.resx`. XAML биндится через `{Binding Source={x:Static loc:LocalizationManager.Instance}, Path=[Key]}` — все меню `MainWindow` и поля `SettingsWindow` локализованы. `Program.cs` пред-загружает настройки и культуру до рендера, чтобы избежать вспышки английского UI на первом кадре. `SaveCommand` в `SettingsViewModel` вызывает `SetCulture(...)` если язык изменился.
- **S6 — Поиск in-document (Ctrl+F)**: `ISearchService` + `SearchService` в Application — итерирует страницы, дёргает `IDocument.GetTextLayerAsync`, ищет case-insensitive substring, собирает `SearchHit`'ы со снипетами (±30 символов контекста). `DocumentTabViewModel` получил `SearchText`, `SearchResults`, `IsSearchVisible`, `IsSearching`, `SelectedSearchHit`, команды `ToggleSearchCommand` / `RunSearchCommand`. Выбор хита прыгает на страницу и перерисовывает её. `MainWindow.xaml`: правый sidebar 320px (показывается при `IsSearchVisible`) с input + кнопкой Find + списком результатов; `Ctrl+F` биндится на `ToggleSearchCommand` через `Window.InputBindings`.
- Ещё **17 тестов**: SearchService (10 — empty/no-match/case-insensitive/multi/cross-page/cap/null-layer/cancel/snippet), DocumentTabViewModel (5 — Title, Toggle, EmptySearch, RunSearch, JumpOnSelect).
- **S7 — Persistent search index (SQLite FTS5)**: `IDocumentIndexer` (Application port). `DocumentIndexingService` — `BackgroundService` + `IDocumentIndexer`; принимает запросы через `Channel<IndexRequest>` (bound=32, DropOldest); для каждого документа вычисляет fingerprint (`IFileFingerprint`) и индексирует все текстовые слои в `IFtsIndex`; ошибки логируются и не роняют сервис. `MainViewModel.OpenDocumentFromPathAsync` вызывает `_indexer.Enqueue(document, path)` сразу после открытия. DI: `DocumentIndexingService` зарегистрирован как `IDocumentIndexer` (singleton) и `IHostedService`. `[assembly: InternalsVisibleTo("Foliant.Infrastructure.Tests")]` добавлен в Infrastructure. Ещё **5 тестов**: `DocumentIndexingServiceTests` (ProcessRequest/fingerprint-ok, FingerprintThrows-swallowed, FtsThrows-swallowed, Cancellation-propagates, Enqueue→background-indexes).
- **S8/A — OCR pipeline foundation**: `OcrOptions`, `IOcrEngine`, `IOcrCache` (Application ports). `OcrPageUseCase` — try-cache → engine → put-cache; ключ = `CacheKey(fp, page, engine.Version, ZoomBucket=0, Flags=0x100)`, бит OcrFlag отделяет OCR-записи от render-записей того же документа/страницы. `OcrDiskCache` — адаптер `IOcrCache` поверх `IDiskCache`: System.Text.Json (source-gen `OcrCacheJsonContext`) + GZip(SmallestSize); порченый payload → `LogWarning` + miss (не throw). Engine version используется как часть ключа → апгрейд Tesseract автоматически инвалидирует старые OCR-записи. DI: `IOcrCache → OcrDiskCache` зарегистрирован; `OcrPageUseCase` будет добавлен в S8/B вместе с `TesseractOcrEngine`. Ещё **12 тестов**: `OcrPageUseCaseTests` (6 — cache-hit, cache-miss-stores, engine-version-in-key, ocr-flag-set, options-passed, null-args), `OcrDiskCacheTests` (6 — miss, roundtrip-with-russian+floats, gzip-shrinks-10KB-to-<500B, corrupt-bytes-swallowed, null-args ×2).
- **S10/A — Annotations foundation (model + persistence)**: `AnnotationKind` (Highlight, StickyNote, Freehand), `AnnotationRect`, `AnnotationPoint` + wide record `Annotation` с фабриками `Highlight`/`StickyNote`/`Freehand` в Domain. `IAnnotationStore` (Application port) — CRUD per-document по fingerprint. `JsonAnnotationStore` (Infrastructure) — sidecar `{LocalAppData}/Foliant/Annotations/{fp}.json`, атомарная запись через `.tmp`+Move, per-document `SemaphoreSlim` через `ConcurrentDictionary`, source-gen `AnnotationsJsonContext`, порченый файл → log + treat as empty. `AppPaths.Annotations` добавлен. DI: `IAnnotationStore` registered. **13 тестов** `JsonAnnotationStoreTests`: empty-list, roundtrip-highlight/note/freehand (с кириллицей и точками), append, update-by-id, update-unknown-throws, remove-true/false, remove-all, restart-survival, 20 concurrent adds сохраняются, document isolation, null-args.
- **S10/B — Annotation service (path-based facade)**: `IFileFingerprint` интерфейс перенесён из `Foliant.Infrastructure.Storage` в `Foliant.Application.Services` (это чистый port — реализация остаётся в Infrastructure). `IAnnotationService` (Application) принимает `documentPath`, внутри получает fingerprint и делегирует в `IAnnotationStore` — ViewModel-слой больше не должен знать про fingerprint. `AnnotationService` (Infrastructure) реализует port. DI: `IAnnotationService → AnnotationService` зарегистрирован. **6 тестов** `AnnotationServiceTests`: list/add/update/remove resolve-fingerprint+delegate, null-annotation в add/update throws.
- **S10/C — Annotations в DocumentTabViewModel**: `DocumentTabViewModel` получил `IAnnotationService` (через factory в `HostBuilder`), `ObservableCollection<Annotation> CurrentPageAnnotations` (фильтр по текущей странице), `LoadAnnotationsAsync(ct)`, `AddHighlightAsync(...)`, `AddNoteAsync(...)`, `RemoveAnnotationCommand`. При `OnCurrentPageIndexChanged` коллекция перефильтровывается. `MainViewModel.OpenDocumentFromPathAsync` дёргает `tab.LoadAnnotationsAsync(ct)` сразу после `Tabs.Add`. Ошибки загрузки логируются и не валят таб. Ещё **7 тестов** `DocumentTabViewModelTests`: load-populates-current-page, page-change-refilters, add-highlight-delegates+appends, add-on-other-page-not-appended, remove-true-drops, remove-false-leaves, load-throws-no-propagate.
- **S11/A — Page navigation + zoom commands + keyboard shortcuts**: `DocumentTabViewModel` получил `NextPageCommand`, `PreviousPageCommand`, `FirstPageCommand`, `LastPageCommand`, `GoToPageCommand(int)` (1-based номер с clamp), `ZoomInCommand` / `ZoomOutCommand` / `ResetZoomCommand`. Константы `MinZoom = 0.10`, `MaxZoom = 8.00`, `ZoomStep = 0.25` (соответствует ZoomBucket-сетке). `OnCurrentPageIndexChanged` и `OnZoomChanged` теперь делают clamp к допустимому диапазону + fire-and-forget `RenderCurrentPageAsync` — пользовательские команды не должны помнить про rerender. Из `OnSelectedSearchHitChanged` убрался дублирующий `_ = RenderCurrentPageAsync` (теперь делается через изменение `CurrentPageIndex`). `MainWindow.xaml` получил `KeyBinding`'и: `PageDown`/`PageUp`, `Ctrl+Home`/`Ctrl+End`, `Ctrl++`/`Ctrl+-`/`Ctrl+0` (плюс NumPad-варианты `Add`/`Subtract`/`NumPad0`) — все биндятся на `SelectedTab.<X>Command`. Ещё **14 тестов** `DocumentTabViewModelTests`: next/prev (с границами), first/last, GoToPage 1-based + clamp, ZoomIn/Out (с MinZoom/MaxZoom-clamp), ResetZoom, ZoomSetter-clamp снизу/сверху.
- **S11/B — Status-bar indicators (`PageInfo`, `ZoomPercent`)**: `DocumentTabViewModel` получил два computed-property: `PageInfo` (формат `"{i+1}/{total}"`, локаль-агностичный) и `ZoomPercent` (целое число процентов). Через `[NotifyPropertyChangedFor]` они автоматически рейзят `PropertyChanged` при смене `CurrentPageIndex`/`PageCount`/`Zoom`. `MainWindow.xaml` статус-бар разделён на два региона: слева — `StatusMessage` от `MainViewModel`, справа — `SelectedTab.PageInfo` и `SelectedTab.ZoomPercent` (`StringFormat='{}{0}%'`). Ещё **4 теста** `DocumentTabViewModelTests`: формат `"5/10"`, propagation `PropertyChanged(PageInfo)` от `CurrentPageIndex`, округление `ZoomPercent` (100/125/50), propagation от `Zoom`.
- **S11/C — Bookmark domain + JSON sidecar persistence**: Domain record `Bookmark(Id, PageIndex, Label, CreatedAt)` + factory `Create`. Application ports `IBookmarkStore` (per-fingerprint CRUD) и `IBookmarkService` (path-based facade с `ToggleAsync` для Ctrl+D-сценария). Infrastructure `JsonBookmarkStore` — sidecar `{LocalAppData}/Foliant/Bookmarks/{fp}.json`, атомарная запись `.tmp+Move`, per-document `SemaphoreSlim` через `ConcurrentDictionary`, source-gen `BookmarksJsonContext`, порченый файл → log + treat as empty. `BookmarkService` (Infrastructure) — мост path → fingerprint → store. `AppPaths.Bookmarks` добавлен. DI: `IBookmarkStore → JsonBookmarkStore` (factory form), `IBookmarkService → BookmarkService`. Ещё **15 тестов**: `JsonBookmarkStoreTests` (9) и `BookmarkServiceTests` (6) — empty list, roundtrip с кириллицей, append, remove true/false, remove-all, restart survival, 20 concurrent adds, document isolation, fingerprint propagation, toggle on/off/different-page.
- **S11/D — Bookmarks в DocumentTabViewModel + Ctrl+D**: `DocumentTabViewModel` получил `IBookmarkService`, отсортированный по `PageIndex` `ObservableCollection<Bookmark> Bookmarks`, `LoadBookmarksAsync(ct)`, `ToggleBookmarkCommand` (label="Page N"), `JumpToBookmarkCommand(Bookmark)`. На toggle коллекция обновляется in-place: добавление вставляется по сортировке, удаление выкидывает по `PageIndex`. `MainViewModel.OpenDocumentFromPathAsync` дёргает `tab.LoadBookmarksAsync(ct)` сразу за `LoadAnnotationsAsync`. `MainWindow.xaml` биндит `Ctrl+D` на `SelectedTab.ToggleBookmarkCommand`. Factory в `AppHostBuilder` обновлён под новый ctor-параметр; `DocumentTabViewModelTests.CreateVm` получил bookmark-mock с тем же null-guard паттерном. Ещё **6 тестов**: load-sorted, toggle-add-inserts-by-page, toggle-remove-on-page, jump-sets-page, jump-null-noop, load-throws-no-propagate.
- **S13/A — License manager skeleton (ECDSA-P256 verifier)**: Domain `License(User, Sku, ExpiresAt, Features)` с `IsExpired(now)` и регистро-нечувствительным `HasFeature`, enum `LicenseStatus` (`Valid`/`Expired`/`Invalid`/`Missing`), record `LicenseValidationResult` с named-фабриками `Valid`/`Expired`/`Invalid`/`Missing`. Application port `ILicenseVerifier.Verify(json, signatureBase64, now)`. Infrastructure `EcdsaLicenseVerifier` — `IDisposable`-обёртка над `ECDsa` с публичным ключом (PEM), source-gen `LicenseJsonContext`. Алгоритм: ECDSA-P256 / SHA-256 над байтами лицензии-JSON; при провале подписи возвращает `Invalid`, при истечении срока — `Expired` (с payload), при битом JSON — `Invalid`. **8 тестов** `EcdsaLicenseVerifierTests`: well-formed-signed-license, tampered-json (User → mallory после подписи), bad-base64-sig, signature-from-different-key, expired-license-payload-still-returned, malformed-json, License.HasFeature case-insensitive, License.IsExpired boundary.
- **S11/E — Multi-tab keyboard navigation**: `MainViewModel` получил `NextTabCommand`/`PreviousTabCommand` (циклический wrap по `Tabs`) и `CloseCurrentTabCommand` (parameterless wrapper над `CloseTabCommand`). `CloseTabAsync` теперь корректно пересаживает selection на соседнюю вкладку при закрытии активной (clamp index). `MainWindow.xaml` биндит `Ctrl+Tab` / `Ctrl+Shift+Tab` / `Ctrl+W` на эти команды. Ещё **6 тестов**: forward/backward cycle с wrap, single-tab/empty-tabs no-op, close-middle reselects neighbor, close-last leaves null. Helper `MakeTabStub` инкапсулирует ctor с растущим списком зависимостей `DocumentTabViewModel`.
- **S13/B — Trial anti-tamper logic (pure managed, no I/O)**: Domain `TrialState(StartedAt, MaxObservedAt, Nonce)`, enum `TrialStatus` (`NotStarted`/`Active`/`Expired`/`Tampered`), record `TrialEvaluation(Status, DaysRemaining, TamperReason)`. Application `TrialAntiTamperService` с константой `TrialDays = 30`, статическими методами `NewTrial(now)` (свежий GUID-нонс), `UpdateMaxObserved(state, now)` (advance high-water mark), `ComputeMarker(state)` (SHA-256 над `StartedAt|Nonce` — не зависит от `MaxObservedAt`, чтобы не требовать обновления маркера на каждом запуске), `Evaluate(primary, secondary, marker, now)`: empty-all → `NotStarted`, partial-empty/state-divergence/marker-mismatch/clock-rollback → `Tampered` (с `TamperReason`), elapsed ≥ 30 days → `Expired`, иначе `Active` с `DaysRemaining`. Откат часов детектится по `max(primary.MaxObservedAt, secondary.MaxObservedAt)`. Файловое хранение DPAPI + reg + marker — следующая часть (S13/C). Ещё **15 тестов** `TrialAntiTamperServiceTests`: not-started, active-fresh/after-10d, expired-after-31d, tampered-on-each-store-missing/divergence/marker-mismatch/clock-backwards, max-across-stores, UpdateMaxObserved newer/older, ComputeMarker определяется только StartedAt+Nonce, NewTrial fresh nonce.

[Unreleased]: https://github.com/flowa7021-source/Reader/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/flowa7021-source/Reader/releases/tag/v0.1.0
