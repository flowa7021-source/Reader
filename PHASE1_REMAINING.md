# Foliant — Phase 1 (Alpha) DoD: оставшиеся работы

**Создан:** 2026-05-31, после merge PR'ов #70–#76.
**Обновлён:** 2026-06-07, после merge PR'ов #77–#134 (Phase 1 annotation/page/OCR треки + Phase 2: подписи, redaction, OCG, metadata, outlines, encrypted-open, print, page labels, Initial View, attachments, insert-pages, XMP, sanitization, named destinations, fonts, links + cycle-guard hardening).
**Базовый контракт:** один PR → draft → merge → следующий от свежего main. Параллелить только pure-application/engine треки, не трогающие `MainWindow.xaml(.cs)` и `Strings*.resx`.

---

## Статус-снимок (после #134 на main, 2026-06-07)

- **Phase 1 (Alpha):** 12/13 спринтов ✅, 1/13 🟡 (S8 OCR — `OcrCerIntegrationTests` готов, остаются бинарные golden-сканы за Windows-стендом).
- **Тесты cross-platform layer** (CI-фильтр, executed cases, реальный прогон `Foliant.CrossPlatform.slnf`): Domain 285 / Application 407 / ViewModels 752 / Engines.Pdf 330 / Infrastructure 242 (+ Epub 28 / Fb2 24 / Mobi 19 / Image 14 / Ocr 25 / DjVu 23 / PerfCompare 6 / CheckCoverage 10). **Итого 2165, 0 failed** на 2026-06-07. (src ≈ 38 000 LOC.)
- **Реальный остаток Phase 1 DoD:** только **D1** (бинарные OCR-сканы) и **E1** (Windows manual smoke + ISCC) — оба требуют Windows-стенда. **Не изменилось** через всю Phase-2 работу (см. ниже — это и есть реальный gate `v0.1.0`).
- **Phase 2 merged (функциональные):** F26 PAdES-B (#103), F32 redaction + find-and-redact (#102/#110), F8 OCG layers (#119/#120), A2b native /Annots 11/11 (#111), /Info metadata editing + UI (#122/#124), /Outlines bookmark writer + Export UI (#123/#125), open password-protected PDF read-side (#126), Print Ctrl+P document-neutral (#128), page labels /PageLabels read+write + UI (#129/#130), Initial View /ViewerPreferences read+write + UI (#130), embedded file attachments /EmbeddedFiles list/extract/add/remove + UI (#131), insert pages from another PDF (#131), XMP metadata /Metadata read+write + UI (#132), JavaScript & actions sanitization scan+remove + UI (#132), named destinations /Names/Dests list/add/remove + UI (#133), fonts listing + UI (#133), link annotations listing + UI (#134).
- **Инжиниринг / hardening merged:** cycle-guard depth-limit на всех рекурсивных cos-обходчиках (`PdfCosLimits.MaxTreeDepth`, #134) — malformed/циклический PDF не валит процесс через StackOverflow.
- **Phase 2 merged (port + honest stub):** F-PdfA (#117, AGPL block), F30/F31 write-side encryption (#118, PdfPig gap). Реальные impl — Phase 3 (verapdf CLI / QPDF), trajectory в PR. (Read-side открытие зашифрованных PDF уже работает — #126.)
- **Инфраструктура merged:** `merge=union` для CHANGELOG (#107), Husky pre-push hook (#112), CS0535 CI-gate (#99), EnableWindowsTargeting Linux compile-check для WPF (док #127).
- **Подтверждено в main (не «todo»):** perf-regression gate, E2E update-check, `SettingsMigrator v1→v2`.

> Полная разбивка волн Phase 2 — в `ROADMAP.md`. Этот файл — только Phase 1 DoD-остаток.

---

## Закрытые треки (этой сессией #70 → #91)

| # | Track | PR | Что закрылось |
|---|---|---|---|
| 1 | R1 sigs read-only | #70 | Q-F25 read-only signature controller |
| 2 | R3 shapes | #71 | Q-F16 5 геометрических аннотаций |
| 3 | R2 stamps | #72 | Q-F18 text-stamps |
| 4 | R4 RTL | #73 | Q-F3 RTL two-page spread |
| 5 | R1 sigs UI | #74 | Q-F25 Tools → View Signatures dialog |
| 6 | D9 XFDF shapes | #75 | Q-F17 XFDF round-trip 10 kinds |
| 7 | D10 FDF shapes | #76 | Q-F17 FDF round-trip 10 kinds |
| 8 | A4 Stamp FDF/XFDF | #77 | Q-F17 = 11/11 kinds |
| 9 | A1 AnnPdf underline+strikeout | #78 | AnnotatedPdf 5/11 |
| 10 | A2 AnnPdf square+circle | #79 | AnnotatedPdf 7/11 |
| 11 | A3 AnnPdf stamp appearance | #80 | AnnotatedPdf 8/11 |
| 12 | L1 MOBI loader | #81 | Q-F1 = 5/5 форматов |
| 13 | B2 form-fill UI | #82 | Q-F24 UI |
| 14 | B3 MOBI Open-filter | #83 | Q-F1 user-flow complete |
| 15 | B1a tool state-machine | #84 | Палитра VM (38 unit tests) |
| 16 | B1b toolbar rect+click | #85 | 6 кнопок: highlight/underline/strikeout/rect/ellipse/note |
| 17 | B1c two-point + freehand | #86 | + line/arrow/freehand |
| 18 | B1c-poly polygon | #87 | + polygon click-to-close |
| 19 | B1d-color swatches + stamp combo | #88 | + 7 цветов + stamp+label |
| 20 | B1d-multipage | #89 | Continuous/two-page wiring |
| 21 | A5 image-stamp Domain+UI+JSON | #90 | Annotation.ImagePath round-trip |
| 22 | D3 S1/S3 perf benchmarks | #91 | BDN-замеры для DoD-acceptance |

## Подтверждено существующим в main (доплан состояние)

- **D1 OCR CER**: `OcrCerIntegrationTests` с порогами Cyrillic ≤2 % / Latin ≤1 %, gated to Windows stand с моделями.
- **D2 Installer**: `installer/Foliant.Installer.InnoSetup/Foliant.iss` (3 tier'а) + `.github/workflows/release.yml` (publish + sign + ISCC × 3 + SHA256SUMS + GH Release).

---

## Дорожная карта (что осталось)

Легенда: `[ ]` не начато · `[~]` в работе / draft PR · `[x]` merged.

### A5b — AnnotatedPdf image-stamp embed
- [x] **Status:** ✅ merged (PR #92)
- **Файлы:** `PdfAnnotationSpec.cs`, `AnnotationToPdfSpec.cs`, `AnnotatedPdfExportService.cs`
- **Acceptance:** `Annotation.ImageStamp(…)` → PDF `/Subtype /Stamp` с embedded image-object (FPDFPageObjNewImageObj + SetBitmap).

### B1e — image-stamp UX
- [x] **Status:** ✅ merged (PR #93)
- **Файлы:** `DocumentTabViewModel.Annotations.cs`, `DocumentTabViewModel.Tools.cs`, `MainWindow.xaml(.cs)`, Strings
- **Acceptance:** Toolbar «Pick image…» / «Clear image» при активном Stamp → создаются image-stamps.

### A2b — line/arrow/polygon native PDF embed
- [x] **Status:** ✅ merged (PR #111, Phase 2) — Q-F17 = 11/11 типов
- **Что:** PDFium 146.x не экспонирует setter'ы для `/L`/`/Vertices`/`/LE`, поэтому реализован
  cos-level fallback — пост-процессинг PDF-байтов после `FPDF_SaveAsCopy` через PdfPig
  (`PdfPigAnnotationAppender` + `PdfIncrementalWriter`, инкрементальный апдейт ISO 32000-1 §7.5.6).
- **Acceptance:** Line/Arrow/Polygon → `/Annots` с корректным `/L`/`/Vertices`/`/LE` — выполнено.

### A5c — XFDF/FDF stamp-image-href round-trip
- [x] **Status:** ✅ done (PR #95 + follow-up edge-cases)
- **Что:** Сохранение `ImagePath` в XFDF (`foliant:imagepath` атрибут в собственном namespace) и FDF
  (`/FoliantImagePath` ключ). Не-портативно для сторонних viewer'ов — они игнорируют незнакомые
  ключи/атрибуты; Foliant↔Foliant round-trip сохраняет путь.
- **Acceptance:** Foliant → XFDF → Foliant сохраняет ImagePath; то же для FDF; не-stamp типы
  никогда не несут ImagePath после round-trip; Unicode / XML-метасимволы / PDF-литералы
  проходят без потерь.
- **Заметка:** JSON round-trip работает с A5 (PR #90); теперь и XFDF/FDF тоже.

### D1 — OCR golden-scan corpus
- [ ] **Status:** инфраструктура готова; нужны `tests/assets/ocr-scan-{ru,en}.png` + `.gt.txt`.
- **Что:** Сгенерировать (или приобрести) эталонные сканы + ground-truth, чтобы `OcrCerIntegrationTests`
  на Windows-стенде делал реальное assertion вместо silent-skip.
- **Acceptance:** S8 OCR → ✅ (модели поставляются — #release.yml; CER ≤ target — этот PR).

### E1 — Windows manual smoke pass + bug-fix sweep
- [ ] **Status:** требует Windows-стенда (не sandboxable)
- **Что:** проверить все 11 аннотаций палитры (drag + click + polygon), все PDFium-mutate треки
  (watermark, header/footer, crop, batch, form-fill, signatures), 5 форматов открытия + RTL toggle.

---

## Итоговая оценка

- Annotation-палитра feature-complete, Q-F1 = 5/5, Q-F17 = 11/11 (native /Annots, в т.ч. A2b
  Line/Arrow/Polygon через #111), Q-F18 image-stamps end-to-end (embed #92 + UX #93), Q-F24/Q-F25
  имеют UI.
- **Остаток до Alpha DoD:** только **D1** (бинарные OCR-сканы) + **E1** (Windows manual smoke +
  ISCC) — оба требуют Windows-стенда и не sandboxable. Всё остальное из исходного плана закрыто.

---

## Out-of-scope для Phase 1 (статус в Phase 2 — см. ROADMAP)

- Q-F5/F6/F7/F9 — inline-редактор с reflow, font matching, inpainting сканов — **заморожено** (Trek 2: 2-й dev).
- Q-F8 — OCG layers editing — **✅ read+toggle merged (#119/#120)**; creation/hierarchy — Phase 2+.
- Q-F22 — XFA с JS-движком — **заморожено** (кандидат на veto, Trek 2).
- Q-F25/F26 — PAdES: B-level **✅ merged (#103)**; T-level (TSA) + revocation — отложено (нужен TSA-сервер).
- Q-F28 — LibreOffice плагин (out-of-process) — **заморожено**.
- Q-F30/F31 — AES-256 + permissions (**запись**) — **port+stub merged (#118)**; real impl Phase 3 (QPDF / raw cos-write). **Чтение** зашифрованных PDF (open + decrypt по паролю) — ✅ merged (#126).
- Q-F32 — redaction — **✅ merged (#102 координатный + #110 find-and-redact)**.
- PDF/A — **port+stub merged (#117)**; real impl Phase 3 (verapdf CLI). PDF/UA, PDF/X — Phase 4.

---

## Обновления

| Дата | Что | Кем |
|---|---|---|
| 2026-05-31 | Файл создан после merge #70–#76 | claude |
| 2026-06-01 | Refresh: добавлены 22 закрытых трека, отмечены подтверждённые D1/D2, обновлён список оставшихся работ | claude |
| 2026-06-01 | A5c → ✅ done: ImagePath round-trip через XFDF/FDF (углы: non-stamp, Unicode, спецсимволы) | claude |
| 2026-06-02 | Phase 2 wave 1 merged: Q-F26 PAdES-B (#103), Q-F32 redaction MVP (#102); wave 3 merged: PDF split (#105), Bates (#106), `.gitattributes` union-merge (#107). Phase 1 DoD-остаток (D1+E1) не изменился — оба Windows-gated. | claude |
| 2026-06-03 | Phase 2 продолжение merged #108–#120: UI-обвязка всех функциональных фич (#113–#116 redaction/bates/split/sig + #120 OCG), A2b native /Annots 11/11 (#111), find-and-redact (#110), F8 OCG real (#119), F-PdfA+F30 port+stub (#117/#118), Husky hook (#112). Тесты 1685→1802. DoD-остаток (D1+E1) по-прежнему не изменился. | claude |
| 2026-06-06 | Phase 2 продолжение merged #121–#128: /Info metadata editing + Document Properties UI (#122/#124), /Outlines writer + Export Bookmarks UI (#123/#125), open password-protected PDF read-side (#126), Print Ctrl+P (#128), docs reconcile (#121) + EnableWindowsTargeting compile-check (#127). Реальный прогон тестов 1802→1868 (0 failed). DoD-остаток (D1+E1) по-прежнему не изменился. | claude |
| 2026-06-07 | Phase 2 продолжение merged #129–#134: page labels read+write + UI (#129/#130), Initial View /ViewerPreferences (#130), attachments /EmbeddedFiles (#131), insert pages from PDF (#131), XMP /Metadata (#132), JS/actions sanitization (#132), named destinations /Names/Dests (#133), fonts listing (#133), link annotations listing (#134), cycle-guard hardening cos-walkers (#134). Реальный прогон тестов 1868→2165 (0 failed). DoD-остаток (D1+E1) по-прежнему не изменился — это и есть реальный gate `v0.1.0` (Windows smoke + ISCC + EV-cert, вне песочницы). | claude |
