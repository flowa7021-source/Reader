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

## Статус-снимок (после #107 на main, 2026-06-02)

- **Phase 1 (Alpha):** 12/13 спринтов ✅, 1/13 🟡 (S8 OCR golden-corpus — Windows-gated).
- **Q-F фичи:** 24 ✅ (Q-F18 image-stamps #92–96; A5c XFDF/FDF #95+#100), **+ 2 Phase-2 partial merged** (Q-F26 PAdES-B #103, Q-F32 redaction MVP #102), 8 заморожены Phase 2+.
- **Тесты cross-platform layer** (CI-фильтр `Category!=Slow&!Integration&!E2E`, executed cases на 2026-06-02):
  - Domain 218 / Application 372 / ViewModels 583 (target gates D90/A80/I70/V60 держатся).
  - Engines.Pdf 121 / Infrastructure 242 / Engines.Epub 28 / Fb2 24 / Mobi 19 / Image 14 / Ocr 25 (1 skip — Windows-only).
  - Plugin.DjVu 23 / Tools.PerfCompare 6 / Tools.CheckCoverage 10.
  - **Итого: 1685 executed (full Slow/Integration набор — ещё больше; см. `dotnet test` без фильтра).**
- **LOC:** ~25 000 в src/, 14 тестовых проектов.
- **Скрытых заглушек нет** — все ограничения явно документированы в коде.

> **⚠️ Дрейф документов (выявлено 2026-06-02).** Планировочные файлы
> (`PHASE1_REMAINING.md` → производный `ROADMAP.md`) систематически отставали
> от кода. По факту **уже в main**, хотя числились как «todo»: perf-regression
> gate (`perf.yml` + `tools/perf-compare`, baseline + threshold 15 + auto-issue),
> E2E update-check (`GitHubUpdateCheckService`/`GitHubReleaseSource` + тесты),
> OCR CER test-runner (`OcrCerIntegrationTests` — нужны лишь бинарные ассеты).
> **Правило на будущее:** перед планированием любой задачи — `grep`/чтение
> кода, не доверять статусу из планов. Реальный остаток Phase 1 DoD —
> **только D1 (бинарные OCR-сканы) + E1 (Windows smoke/ISCC)**, оба не
> sandboxable; всё остальное из «Trek 0» — закрыто.

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

### Волна 4 — кандидаты (in-flight / следующая итерация)

- **W0** (DI-only): зарегистрировать 3 merged-сервиса (Redaction/Split/Bates) в `AppHostBuilder` + расширить `DocumentTabViewModel` factory. **Без UI** — отдельная серия W1–W4.
- **W1/W2/W3/W4** (UI-серия): диалоги + меню для Redaction / Bates / Split + Sig-UX green-OK banner. Серийно (трогают `MainWindow.xaml`+`Strings*.resx`).
- **C1**: A2b native /Annots embed для Line/Arrow/Polygon через PdfPig cos-write (закрывает Q-F17 = 11/11 native).
- **C2**: F32 follow-up — `SearchService` отдаёт bbox + новый `IFindAndRedactService` (find-and-redact wrapper).
- **C3**: Husky.Net pre-push hook — автоматизирует чек-лист из `DEV_RETROSPECTIVE` §3.

### Отложить (Волна 5+)

- **F26 follow-up T-level/revocation** — очень высокая сложность (TSA-сервер для тестов, OCSP/CRL HTTP-клиент), 2–3 спринта.
- **F30** AES-256 + 8 permissions — требует архитектурного решения (QPDF внешний бинарь vs raw cos-write через PdfPig).
- **F-PdfA** — нужен veraPDF NuGet; средняя сложность, низкий приоритет.
- **F8 OCG editing** — нет PDFium-bindings; требует cos-level write.
- **D6b/D8b** EPUB/FB2/MOBI визуальный рендер — стратегическое решение (Trek 2 п.3).
- **E1 inline-editor** — стратегическое решение (Trek 2 п.4 — второй разработчик).
- **D6b/D8b** — визуальный рендер EPUB/FB2/MOBI (layout-движок, очень высокая сложность). Не для параллельного спрея — отдельный фокус-спринт.

P4, P5 — следующая волна после merge первой.

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
