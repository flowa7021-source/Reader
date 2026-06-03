# Changelog

Все заметные изменения проекта документируются здесь.

Формат: [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/).
Версии: [Semantic Versioning 2.0.0](https://semver.org/lang/ru/).

## [Unreleased]

### Added
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
