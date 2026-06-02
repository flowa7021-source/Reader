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

## Статус-снимок

- **Phase 1 (Alpha):** 12/13 спринтов ✅, 1/13 🟡 (S8 OCR golden-corpus).
- **Q-F фичи:** 23 ✅, 1 ✅ (Q-F18 image-stamps закрыт в #92–96), 9 заморожены Phase 2+.
- **Тесты:** Domain 218 / Application 362 / ViewModels 583, gates D90/A80/I70/V60.
- **LOC:** ~25 000 в src/, ~1 100+ юнит-тестов в tests/.
- **Скрытых заглушек нет** — все ограничения Phase 1 явно документированы в коде.

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
| 2 | Cumulative perf-regression gate (15% p95) в CI | `.github/workflows/perf.yml`, `tools/perf-compare/` | ✅ pure CI/tooling |
| 3 | Backwards-compat миграции `AppSettings`/`license.key`/`trial.dat` | `Foliant.Infrastructure/Settings/SettingsMigrator.cs` | ✅ pure Infrastructure |
| 4 | Crash reporter opt-in UI-toggle + upload-канал | `Foliant.Infrastructure/Diagnostics/`, `SettingsWindow.xaml` | ⛔ MainWindow/Strings |
| 5 | F1-справка in-app | новый `HelpService` + WebView2 over docs/ | ⛔ MainWindow/Strings |
| 6 | CI-gate на `CS0535` (interface drift) | `.github/workflows/ci.yml` | ✅ pure CI |
| 7 | Pre-push checklist из DEV_RETROSPECTIVE §3 как Husky.Net hook | `tools/`, `.husky/` | ✅ pure tooling |

---

## Пул задач для параллельного выполнения сейчас

Изолированные по файлам треки, готовые к **немедленному** запуску
параллельных PR'ов (каждый ≤ 300 LOC, draft → merge → next):

| # | Track | Файлы | Зависимости | Сложность | Шт-агентов |
|---|---|---|---|---|---|
| **P1** | A5c — `Annotation.ImagePath` round-trip в XFDF + FDF через `foliant:imagePath` custom-атрибут | `src/Foliant.Application/Services/{Xfdf,Fdf}Annotation{Exporter,Importer}.cs` + tests | нет | Низкая | 1 |
| **P2** | Honesty disclaimer EPUB/FB2/MOBI «text-only view» | `README.md`, `docs/user-guide/ui-tour.md` | нет | Тривиальная | 1 |
| **P3** | CI-gate на `CS0535` (interface drift) | `.github/workflows/ci.yml` | нет | Низкая | 1 |
| **P4** | Test-skeleton для D1 OCR golden corpus (test runner, asset-format docs, без PNG) | `tests/Foliant.Engines.Ocr.Tests/`, `tests/assets/README-OCR.md` | нет | Средняя | 1 |
| **P5** | Backwards-compat миграция `AppSettings` v1→v2 (заготовка под schema-version bump) | `Foliant.Infrastructure/Settings/SettingsMigrator.cs` + tests | нет | Низкая | 1 |

P1+P2+P3 запускаются прямо сейчас в **3 параллельных worktree-агентах**:
у них **нулевое пересечение по файлам**, нет общих зависимостей, каждый PR ≤ 200 LOC.

P4, P5 — следующая волна после merge первой.

---

## Acceptance для S14 (после merge всех P1–P5)

- [ ] `Annotation.ImagePath` сохраняется/читается через XFDF и FDF (Foliant ↔ Foliant).
- [ ] README + ui-tour указывают: EPUB/FB2/MOBI открываются для поиска и навигации,
      но визуальный рендер — Phase 2 (D6b/D8b).
- [ ] CI ломается при попытке смержить интерфейс без всех реализаций.
- [ ] `OcrCerIntegrationTests` готовы запуститься на Windows-стенде, как только
      будут добавлены `tests/assets/ocr-scan-{ru,en}.png` + `.gt.txt`.
- [ ] `SettingsMigrator` готов поднять v1 → v2 при первой необходимости.
- [ ] Тег `v0.1.0` — только после S15 (Windows smoke + sign + ISCC).

---

## История

| Дата | Изменения |
|---|---|
| 2026-06-01 | Файл создан. Roadmap фазы 2 разнесён на 3 потока (A/B/C). Определён пул P1–P5 для параллельного выполнения. |
