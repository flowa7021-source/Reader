# Foliant — Roadmap (post-Alpha)

**Создан:** 2026-06-01, после merge PR #70–#96 (Phase 1 ≈ feature-complete).
**Базовый контракт:** один PR → draft → merge → следующий от свежего main.
**Параллелить можно только pure-application/engine/docs треки, не трогающие
`MainWindow.xaml(.cs)` и `Strings*.resx`.**

> Этот документ — **operational** roadmap для треков и PR'ов. Стратегические
> решения и фазы — в [`PROJECT_BOARD.md`](PROJECT_BOARD.md). Детальный план
> и контракт качества кода — в [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md).
> Оставшиеся хвосты Phase 1 — в [`PHASE1_REMAINING.md`](PHASE1_REMAINING.md).

---

## Статус-снимок (после #132 на main, 2026-06-07)

- **Phase 1 (Alpha):** 12/13 спринтов ✅, 1/13 🟡 (S8 OCR golden-corpus — Windows-gated).
- **Q-F фичи:** 24 ✅ (Phase 1) **+ Phase-2 merged**: Q-F26 PAdES-B (#103), Q-F32 redaction + find-and-redact (#102/#110), Q-F8 OCG layers (#119/#120), Q-F17 11/11 native annots (#111), **/Info metadata editing (#122 + UI #124)**, **/Outlines bookmark writer + export (#123 + UI #125)**, **open password-protected PDF (#126)**, **Print Ctrl+P (#128)**, **page labels /PageLabels (#129/#130)**, **Initial View /ViewerPreferences (#130)**, **embedded file attachments /EmbeddedFiles (#131)**, **insert pages from another PDF (#131)**, **XMP metadata /Metadata read+write + UI (#132)**, **JavaScript & actions sanitization (#132)**. **+ port+stub** (честный долг): Q-F-PdfA (#117), Q-F30/F31 write-side encryption (#118).
- **Тесты cross-platform layer** (CI-фильтр `Category!=Slow&!Integration&!E2E`, executed cases, реальный прогон `Foliant.CrossPlatform.slnf`):
  - Domain 285 / Application 407 / ViewModels 731 (target gates D90/A80/I70/V60 держатся).
  - Engines.Pdf 283 / Infrastructure 242 / Engines.Epub 28 / Fb2 24 / Mobi 19 / Image 14 / Ocr 25.
  - Plugin.DjVu 23 / Tools.PerfCompare 6 / Tools.CheckCoverage 10.
  - **Итого: 2097 executed, 0 failed (на main после #132; full Slow/Integration набор — ещё больше).**
- **LOC:** ~37 000 в src/, 14 тестовых проектов.
- **Скрытых заглушек нет** — F-PdfA/F30 (write-side) stubs бросают `NotSupportedException` с явным маркером + документированным Phase 3 trajectory.

> **⚠️ Дрейф документов — рецидивирующий паттерн.** Планы систематически отстают
> от кода (выявлено 2026-06-02, повторно 2026-06-03 после #108–#120, и снова
> 2026-06-06 после #121–#128). **Правило:** перед планированием любой задачи —
> `grep`/чтение кода + `dotnet test` для цифр, не доверять статусу из планов
> (цифры выше — из реального прогона, не из памяти). `CHANGELOG.md` защищён
> `merge=union` (#107) — параллельные PR больше не конфликтуют по `[Unreleased]`
> (проверено многократным авто-rebase). Реальный остаток Phase 1 DoD — **только
> D1 (бинарные OCR-сканы) + E1 (Windows smoke/ISCC)**, оба не sandboxable.

---

## Trek 0 — Закрыть Phase 1 DoD (S14–S15)

**Цель:** тег `v0.1.0` с реальным perf-baseline и Windows smoke pass.

### S14 — «Alpha lock» (2 недели)

| # | Track | Файлы | Acceptance | Параллель |
|---|---|---|---|---|
| A5c | XFDF/FDF stamp-image-href round-trip | `XfdfAnnotationExporter`, `XfdfAnnotationImporter`, `FdfAnnotationExporter`, `FdfAnnotationImporter` + tests | Foliant → XFDF/FDF → Foliant сохраняет `Annotation.ImagePath` через `foliant:imagePath` custom-атрибут | ✅ pure Application |
| Honesty | Disclaimer EPUB/FB2/MOBI «text-only» | `README.md`, `docs/user-guide/ui-tour.md` | Пользователь понимает: 5 форматов open, но 3 из них read-only-через-text-layer (без визуального рендера) | ✅ pure docs |
| D1 | OCR golden-scan corpus | `tests/assets/ocr-scan-{ru,en}.png` + `.gt.txt` | `OcrCerIntegrationTests` на Windows — реальное assertion (CER рус ≤2 %, eng ≤1 %), не silent-skip | ⛔ требует Windows-стенд |
| Perf | S1/S3 baseline на Windows | `tests/Foliant.Performance/baseline.json` | Замеры p95 рендера, open 100-стр, search 10-docs, OCR-page, cold-start | ⛔ требует Windows-стенд |

### S15 — «Windows smoke + bugfix sweep» (2 недели)

| # | Track | Acceptance | Параллель |
|---|---|---|---|
| E1 | Manual smoke 11 annot tools + watermark/HF/crop/merge/form-fill/sig view + 5 форматов + RTL | По `tests/manual/RELEASE_SMOKE.md` ×2 машины (Win10 21H2 + Win11) | ⛔ Windows-only |
| Inst | ISCC × 3 tier + sign-binaries.ps1 end-to-end | Install/uninstall чистый на чистой VM | ⛔ Windows-only |
| BFix | Bugfix sweep всех находок smoke | Зелёный CI после правок | ⛔ зависит от smoke |
| Rel | Финализировать `v0.1.0` release-notes и GH Release | Tag pushed | ⛔ зависит от smoke |

---

## Trek 1 — Phase 2 (Beta), 3 параллельных потока

### Поток A — «Полнота альфы → почти-1.0» (4–5 спринтов)

| # | Что | Файлы | Сложность | Параллель |
|---|---|---|---|---|
| A2b | Line/Arrow/Polygon native `/Annots` embed | `PdfAnnotationSpec.cs`, `AnnotatedPdfExportService.cs` (PdfPig пост-процессинг) | Высокая | ✅ pure Engine |
| D6b | EPUB визуальный рендер (WebView2 → BGRA32 или AngleSharp+Skia) | `EpubDocument.RenderPageAsync`, новый renderer-компонент | Высокая | ✅ pure Engine |
| D8b | FB2/MOBI визуальный рендер (XML→HTML→bitmap) | `Fb2Document.RenderPageAsync`, `MobiDocument.RenderPageAsync` | Высокая | ✅ pure Engine |
| Sig-UX | Banner «требует ручной проверки» при `IsValid=false` | `SignaturesDialog.xaml`, `Strings*.resx` | Низкая | ⛔ MainWindow/Strings |
| Upd | E2E update notification через реальный GH Releases | `GitHubUpdateCheckService`, тесты | Низкая | ✅ pure Infrastructure |

### Поток B — Pro-фичи (6–8 спринтов, открывает монетизацию)

| # | Что | Файлы | Сложность | Параллель |
|---|---|---|---|---|
| F26 | Full PAdES B+T validation (cert chain + TSA + CRL/OCSP) | `PdfSignatureController.ValidateAsync`, новый `PadesValidator` | Очень высокая | ✅ pure Engine |
| F30 | AES-256 шифрование PDF + 8 permission-флагов | новый `PdfCryptoService`, через QPDF/raw cos-write | Высокая | ✅ pure Engine |
| F32 | Полное redaction (find-and-redact + physical removal) | новый `PdfRedactionService` | Высокая | ✅ pure Application/Engine |
| F-PdfA | PDF/A валидация через veraPDF | новый `PdfAValidationService` | Средняя | ✅ pure Application |

### Поток C — Inline-редактор (8–12 спринтов, **самый рискованный**)

| # | Что | Файлы | Сложность | Параллель |
|---|---|---|---|---|
| E1 | Простой text editor: замена одной строки on-the-spot | `PdfCommands`, `PdfCommandDispatcher`, `PdfDocumentEditor` + новые VM-команды | Очень высокая | ✅ pure Engine/VM (но конфликтует со старой Editing.cs) |
| E2 | Reflow в пределах одного абзаца (`/Tj` `/TJ` regen) | `Foliant.Pro.AdvancedEditor` (новый проект, закрытый) | Очень высокая | ✅ изолировано в Pro/ |
| F8 | OCG editing (layers add/remove/rename) | новый `PdfOcgService` | Средняя | ✅ pure Engine |

> ⚠️ Поток C съест все ресурсы соло-разработчика. Опции из reality-check
> (PROJECT_BOARD §0): либо изолировать в `pro/` с feature flag и feature-gate,
> либо найти 2-го разработчика, либо вычеркнуть до 2.0.

---

## Trek 2 — Стратегические решения (нужны до старта Phase 2)

| # | Решение | Срок | Кто |
|---|---|---|---|
| 1 | **XFA с JS — вычеркнуть или сохранить?** Висит «кандидат на veto». Вычеркивание = экономия 6–9 чел-мес. | До S14 | Владелец продукта |
| 2 | **EV-сертификат code signing** (50–500 тыс ₽/год). Без него SmartScreen = нет пользователей. | Critical-gate до v0.1.0 | Финансы |
| 3 | **EPUB/FB2/MOBI визуальный рендер — Phase 2 или вычеркнуть?** Если оставить read-only — disclaimer должен быть в UI и README (S14). | До S14 | Владелец продукта |
| 4 | **2-й разработчик для Потока C** (inline-editor). Без него Phase 3 = 30–40 месяцев. | До старта Phase 2 | Найм |
| 5 | **Brand check** — TM Роспатент / USPTO TESS / EUIPO eSearch. Коллизия с OSS-Foliant (foliant-docs) не разрешена. | До любого публичного релиза | Юристы |

---

## Trek 3 — Технический долг (фоном)

| # | Что | Где | Параллель |
|---|---|---|---|
| 1 | Расширенный perf-baseline (cold-start, OCR p95, search-10docs p95) | `tests/Foliant.Performance/baseline.json` | ⛔ требует Windows |
| 2 | ~~Cumulative perf-regression gate (15% p95) в CI~~ — **✅ уже в main** (`perf.yml` сравнивает с baseline, порог 15, auto-issue) | `.github/workflows/perf.yml`, `tools/perf-compare/` | ✅ done |
| 3 | Backwards-compat миграции `AppSettings`/`license.key`/`trial.dat` | `Foliant.Infrastructure/Settings/SettingsMigrator.cs` | ✅ pure Infrastructure |
| 4 | Crash reporter opt-in UI-toggle + upload-канал | `Foliant.Infrastructure/Diagnostics/`, `SettingsWindow.xaml` | ⛔ MainWindow/Strings |
| 5 | F1-справка in-app | новый `HelpService` + WebView2 over docs/ | ⛔ MainWindow/Strings |
| 6 | ~~CI-gate на `CS0535` (interface drift)~~ — **✅ merged #99** | `.github/workflows/verify.yml` | ✅ done |
| 7 | Pre-push checklist из DEV_RETROSPECTIVE §3 как Husky.Net hook | `tools/`, `.husky/` | ✅ pure tooling |

---

## Пул задач для параллельного выполнения

### Волна 1 — ✅ merged (2026-06-01)

| # | Track | PR | Статус |
|---|---|---|---|
| **P1** | A5c — `Annotation.ImagePath` round-trip в XFDF + FDF | #100 | ✅ (impl был в #95; #100 добавил edge-case тесты) |
| **P2** | Honesty disclaimer EPUB/FB2/MOBI «text-only» | #98 | ✅ |
| **P3** | CI-gate на `CS0535` (interface drift) | #99 | ✅ |

> P4 (OCR test-skeleton) и P5 (SettingsMigrator) сняты: проверка кода показала,
> что `OcrCerIntegrationTests` уже полнофункционален (нужны лишь бинарные
> ассеты — D1, Windows-gated), а спекулятивная v1→v2 миграция нарушает YAGNI.

### Волна 2 — ✅ merged (2026-06-02)

Крупные Phase-2 фичи, изолированные по файлам, верифицируемые на Linux
(PDFium + BouncyCastle 2.5.0 работают в sandbox; `Engines.Pdf.Tests` ∈ CrossPlatform slnf):

| # | Track | PR | Что закрылось |
|---|---|---|---|
| **F26** | PAdES-B валидация подписи (CMS verify + ByteRange integrity + cert chain) | #103 | `PadesValidator.cs` + `ByteRangeParser.cs`; `ValidateAsync` больше не stub. T-level (TSA) / revocation (CRL/OCSP) — отдельный follow-up (см. Волну 4). |
| **F32** | Physical redaction MVP (strip intersecting text + opaque box) | #102 | `IRedactionService` + `PdfiumRedactionService` (координатный MVP). Find-and-redact по тексту/regex — отдельный follow-up (Волна 3 — C2). |

### Волна 3 — ✅ merged (2026-06-02, late)

Параллельный пул дополнительных фич с нулевым пересечением файлов:

| # | Track | PR | Что закрылось |
|---|---|---|---|
| Split | `IPdfSplitService` / `PdfPigSplitService` — split-every-N + non-contiguous selection | #105 | 14 тестов, pure-managed PdfPig. DI/UI wiring — отдельный PR. |
| Bates | `IBatesNumberingService` / `PdfiumBatesNumberingService` — юридическая нумерация страниц | #106 | 13 тестов, PDFium. Монотонный счётчик по абсолютному индексу. DI/UI wiring — отдельный PR. |
| Git | `CHANGELOG.md merge=union` в `.gitattributes` | #107 | Профилактика: параллельные PR больше не конфликтуют по `[Unreleased]`. |

### Волна 4 — ✅ merged (2026-06-02): пачка 1 (параллельная) + C-треки

| # | Track | PR |
|---|---|---|
| **W0** | DI-only: Redaction/Split/Bates в `AppHostBuilder` + factory | #108 |
| **C1** | A2b native /Annots embed (Line/Arrow/Polygon, cos-write) — Q-F17 = 11/11 | #111 |
| **C2** | F32 follow-up — `SearchHit.Bbox` + `IFindAndRedactService` | #110 |
| **C3** | Husky.Net pre-push hook (format + build + fast tests) | #112 |
| docs | reconcile #1 + bad-xref fix | #109, #104 |

### Волна 5 — ✅ merged (2026-06-02/03): UI-серия (серийная) + Phase-2 фичи

UI-серия (серийно — общий `MainWindow.xaml`/`Strings`/`PdfEffects.cs`):

| # | Track | PR | Заметка |
|---|---|---|---|
| **W1** | Redaction UI (Find-and-Redact dialog) | #113 | |
| **W2** | Bates UI | #114 | + CS0108 fix (WPF reserved-name `FontSize`) |
| **W3** | Split UI | #115 | + CS1734 fix (paramref в class-doc) |
| **W4** | Sig-UX green-OK banner | #116 | colour-coded validation |
| **W5** | OCG layers panel + DI | #120 | |

Phase-2 фичи (параллельно, изолированы):

| # | Track | PR | Результат |
|---|---|---|---|
| **F8** | OCG layers read + visibility toggle | #119 | real engine (PdfPig cos-write; PDFium 146 без OCG API) |
| **F-PdfA** | PDF/A validation port | #117 | port + honest stub (managed wrapper AGPL — license block) |
| **F30** | AES-256 encryption + 8 permissions port | #118 | port + honest stub (PdfPig не пишет encrypted) |

> **Урок WPF blind-spot**: `Foliant.UI` (`net10.0-windows`) не компилируется на Linux →
> ни cross-platform build, ни pre-push hook не видят C#-диагностику code-behind.
> CS0108 (#114) и CS1734 (#115) поймал только Windows-CI. **Правило для WPF-PR:**
> избегать reserved-имён членов `Window`/`Control` (`FontSize`/`Width`/`Content`/…),
> `<paramref>` только в doc метода с этим параметром; перед коммитом — grep-self-check.

### Волна 6 — ✅ merged (2026-06-03/06): metadata + outlines + encrypted-open + print

| # | Track | PR | Результат |
|---|---|---|---|
| **Meta** | `/Info` metadata editing (Title/Author/Subject/Keywords/Creator/Producer) | #122 | PdfPig `PdfMerger` + `DocumentInformationBuilder`; `null`=«не менять», ""=«очистить»; pure-managed, 14 тестов. |
| **Meta-UI** | Document Properties dialog + DI | #124 | `DocumentPropertiesDialog`, empty-field = «не менять», 10 VM-тестов. |
| **Outline-W** | `/Outlines` writer — встраивание закладок обратно в PDF (симметрично reader) | #123 | `PdfPigOutlineWriter` (cos incremental write, nested по Depth, Unicode UTF-16BE), 12 тестов. |
| **Outline-UI** | Export Bookmarks to PDF + DI | #125 | `DocumentTabViewModel.OutlineExport`, nested round-trip с Import PDF Outline, 11 VM-тестов. |
| **Pwd-Open** | Open password-protected PDF — read-side decrypt + prompt/retry | #126 | `IPasswordAwareDocumentLoader` + `DocumentPasswordRequiredException` + `IPasswordPrompt`; PDFium decrypt (AES/RC4), 3 App + 4 VM + 4 Engine теста. |
| **Print** | Print (File → Print, Ctrl+P) document-neutral via `IDocument.RenderPageAsync` | #128 | `IPrintService` + `WpfPrintService` (WPF `PrintDialog` → `FixedDocument`), работает для всех форматов, 11 VM-тестов. |
| docs | reconcile #3 (#121) + EnableWindowsTargeting Linux compile-check (#127) | #121, #127 | ROADMAP/PHASE1 sync; WPF-compile-check на Linux задокументирован в `docs/BUILD.md`. |

### Волна 7 — ✅ merged: page labels UI + Initial View (#130)

- **Page labels (`/PageLabels`)** — движок (#129) + UI wiring (#130): `DocumentTabViewModel.PageLabels`
  + `PageLabelsDialog` («Number Pages») + меню + L10n.
- **Viewer Preferences / Initial View** (#130) — полный стек read+write (Domain + port + engine cos) +
  DI + VM + `ViewerPreferencesDialog` + меню **File → Initial View…** + L10n.

### Волна 8 — ✅ merged: attachments + insert pages (#131)

- **Embedded file attachments (`/EmbeddedFiles`)** (#131) — list/extract/add/remove полным стеком
  (первый stream-объект writer) + `AttachmentsDialog` + меню **File → Attachments…**.
- **Insert pages from another PDF** (#131) — `IPdfInsertPagesService` (PdfPig `PdfMerger`) +
  `InsertPagesDialog` + меню **File → Insert Pages from PDF…**.

### Волна 9 — ✅ merged: XMP metadata + sanitization (#132)

- **XMP metadata (`/Metadata`)** (#132) — read+write полным стеком + `XmpMetadataDialog` + меню
  **File → XMP Metadata…**. (Закрыт Wave-7-кандидат «Metadata XMP».)
- **JavaScript & actions sanitization** (#132) — scan + remove (`/OpenAction` JS / `/Names/JavaScript` /
  catalog `/AA`) + `SanitizationDialog` + меню **File → Remove JavaScript & Actions…**.

### Волна 10 — in-flight + кандидаты

- **Named destinations (`/Names/Dests` + legacy `/Dests`)** — **in-flight (текущий PR)**: list/add/remove
  полным стеком (читает обе формы, пишет модерн) + `NamedDestinationsDialog` + меню
  **File → Named Destinations…**. 29 движковых + 11 VM-тестов. Движок суб-агентом в worktree.
- **Document fonts listing** — **in-flight (текущий PR)**: read-only список шрифтов с embedding-статусом
  (Document Properties → Fonts) + `FontsDialog` + меню **File → Fonts…**. 8 движковых + 5 VM-тестов.
- **OCG UI follow-up**: панель слоёв сейчас modal-dialog; live-toggle в sidebar — улучшение UX.
- **Sig-UX banner** (Trek 1 Поток A): «требует ручной проверки» при `IsValid=false` — ещё не сделан.
- **Outline richness**: named destinations, open/closed (знак `/Count`), цвета/стили (`/C`/`/F`), XYZ-zoom — writer сейчас пишет только GoTo-page `/Fit`.
- **Print follow-up**: scale-to-fit / fit-to-margins, N-up, двусторонняя — сейчас 1 страница = 1 лист в натуральном размере.

### Отложить (требуют стратегического решения / внешних runtime)

- **F-PdfA real impl** — `verapdf` CLI out-of-process plugin (`plugins/Foliant.Plugin.VeraPdf`, паттерн DjVu). Нужен JRE + jar в инсталляторе. Trajectory в #117.
- **F30 real impl** — QPDF embed (+5 MB nativka) **или** raw cos-write + BouncyCastle (1–2 спринта). Trajectory в #118.
- **F26 follow-up T-level/revocation** — TSA-сервер для тестов, OCSP/CRL HTTP-клиент.
- **D6b/D8b** EPUB/FB2/MOBI визуальный рендер — layout-движок; стратегическое решение (Trek 2 п.3).
- **E1 inline-editor** — стратегическое решение (Trek 2 п.4 — второй разработчик).

---

## Acceptance для S14 (Alpha lock)

- [x] `Annotation.ImagePath` сохраняется/читается через XFDF и FDF (Foliant ↔ Foliant) — #95 + #100.
- [x] README + ui-tour указывают: EPUB/FB2/MOBI открываются для поиска и навигации,
      но визуальный рендер — Phase 2 (D6b/D8b) — #98.
- [x] CI ломается при попытке смержить интерфейс без всех реализаций — #99.
- [x] `OcrCerIntegrationTests` готовы запуститься на Windows-стенде, как только
      будут добавлены `tests/assets/ocr-scan-{ru,en}.png` + `.gt.txt` — уже в main.
- [ ] **D1** — бинарные OCR-сканы + ground-truth (Windows-gated, единственный
      content-остаток S14).
- [ ] Тег `v0.1.0` — только после S15 (Windows smoke + sign + ISCC).

---

## История

| Дата | Изменения |
|---|---|
| 2026-06-01 | Файл создан. Roadmap фазы 2 разнесён на 3 потока (A/B/C). Определён пул P1–P5 для параллельного выполнения. |
| 2026-06-02 (день) | Reconcile с реальностью: волна 1 (P1–P3) merged #98/#99/#100; выявлен дрейф документов (perf-gate/Upd/OCR-runner уже в main); запущена волна 2 (F26 PAdES-B, F32 redaction). Реальный остаток Phase 1 = D1 + E1 (Windows-gated). |
| 2026-06-02 (вечер) | Волна 2 merged (#102 F32, #103 PAdES-B), волна 3 merged (#105 split, #106 Bates, #107 gitattributes union-merge), bad-xref fix (#104). Reconcile #2: повторный дрейф (Trek 3 пункт 2 perf-gate / пункт 6 CS0535 — оба уже в main). Запущена волна 4: W0 DI-only, C1 A2b native /Annots, C2 F32-follow-up find-and-redact, C3 Husky pre-push. |
| 2026-06-03 | Волна 4 merged (#108 W0, #110 C2, #111 C1, #112 C3). Волна 5 merged: UI-серия #113–#116 (W1 redaction / W2 bates +CS0108 / W3 split +CS1734 / W4 sig-banner), #120 W5 OCG UI; Phase-2 фичи #117 F-PdfA stub / #118 F30 stub / #119 F8 OCG real. Reconcile #3: цифры тестов 1685→1802, добавлен урок WPF blind-spot (CS0108/CS1734 ловит только Windows-CI). Запущена волна 6: metadata /Info editing. |
| 2026-06-06 | Волна 6 merged (#121–#128): /Info metadata editing + Document Properties UI (#122/#124), /Outlines writer + Export Bookmarks UI (#123/#125), open password-protected PDF read-side (#126), Print Ctrl+P document-neutral (#128), docs reconcile (#121) + EnableWindowsTargeting compile-check (#127). Reconcile #4: реальный прогон тестов 1802→1868 (0 failed), Волна 6 переведена in-flight→merged, заведена Волна 7 (OCG live-toggle, sig-UX banner, XMP, outline richness, print follow-up). DoD-остаток (D1+E1) не изменился. |
| 2026-06-06 (поздно) | Волна 6.x merged: page labels /PageLabels read+write движок (#129, 56 тестов). Запущен крупный PR Волны 7 «navigation & initial view»: page-labels UI wiring (DI + PageLabelsDialog + меню) **+** новая фича Viewer Preferences / Initial View полным стеком (Domain + port + cos engine + DI + VM + ViewerPreferencesDialog + меню + L10n). Engine Viewer Preferences собран суб-агентом в изолированном worktree, retrieved по SHA. Реальный прогон тестов 1924→1981 (0 failed). WPF верифицирован compile-check'ом EnableWindowsTargeting на Linux. |
| 2026-06-06 (ночь) | Волна 7 merged (#130): page-labels UI + Initial View. Запущен крупный PR Волны 8 «attachments & page assembly»: embedded file attachments (list/extract/add/remove, первый stream-объект writer) **+** insert pages from another PDF — обе фичи полным стеком (engine суб-агентами в worktree'ах, retrieved по SHA; DI/VM/dialogs/menu/L10n/тесты — в основном дереве). Реальный прогон тестов 1978→2050 (+72, 0 failed). 2 движковых агента + ревью-агент параллельно; usage без лимитов. Reconcile: #130-снимок завышал Engines.Pdf на 3 (207 vs 204) — исправлено. |
| 2026-06-07 | Волна 8 merged (#131): attachments + insert pages. Запущен крупный PR Волны 9 «documents of record: XMP & sanitization»: XMP metadata (/Metadata read+write) **+** document JavaScript & actions sanitization (scan + remove) — обе полным стеком (engine суб-агентами в worktree'ах, retrieved по SHA). Реальный прогон тестов 2050→2097 (+47, 0 failed). Закрыт Wave-7-кандидат «Metadata XMP»; sanitization закрывает /Names/Dests-gap из #131 (тест на сохранение sibling-ключей). |
| 2026-06-07 (поздно) | Волна 9 merged (#132): XMP + sanitization. Запущен крупный PR Волны 10 «navigation & inspection»: named destinations (/Names/Dests + legacy /Dests, list/add/remove) **+** document fonts listing (Document Properties → Fonts, read-only) — обе полным стеком (engine суб-агентами в worktree'ах, retrieved по SHA). Реальный прогон тестов 2097→2150 (+53, 0 failed). |
