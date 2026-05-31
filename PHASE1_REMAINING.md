# Foliant — Phase 1 (Alpha) DoD: оставшиеся работы

**Создан:** 2026-05-31, после merge PR'ов #70–#76.
**Базовый контракт:** один PR → draft → merge → следующий от свежего main. Параллелить только pure-application/engine треки, не трогающие `MainWindow.xaml(.cs)` и `Strings*.resx`.

---

## Статус-снимок (на момент создания плана)

- **Phase 1 (Alpha):** 10/13 спринтов ✅, 3/13 🟡.
- **Q-F фичи:** 19 ✅, 4 🟡, 9 заморожены Phase 2+.
- **Тесты:** ~1431 кейсов; coverage gates держатся (D 99.7 / A 85.5 / I 80.7 / V 88.2).
- **Свежемерджено:** #70 sigs read-only • #71 shapes • #72 stamps • #73 RTL • #74 sigs UI • #75 XFDF shapes • #76 FDF shapes.

---

## Дорожная карта (рекомендованный порядок принят)

Легенда: `[ ]` не начато · `[~]` в работе / draft PR · `[x]` merged.

### 1. A4 — Stamp в FDF/XFDF round-trip
- [x] **Status:** merged (#77)
- **Файлы:** `XfdfAnnotationExporter.cs`, `XfdfAnnotationImporter.cs`, `FdfAnnotationExporter.cs`, `FdfAnnotationImporter.cs` + tests
- **Acceptance:** Stamp round-trip через оба формата; tests на label + bounds + color preservation
- **Размер:** S
- **PR:** _tbd_

### 2. A1 — AnnotatedPdf: underline + strikethrough
- [x] **Status:** merged (#78)
- **Файлы:** `AnnotatedPdfExportService.cs`, `AnnotationToPdfSpec.cs` + tests
- **Acceptance:** `/Subtype /Underline` и `/Subtype /StrikeOut` появляются в `/Annots`; QuadPoints корректны
- **Размер:** S
- **Блокирует:** A2 (общие helpers)
- **PR:** #78

### 3. A2 — AnnotatedPdf: shapes (square/circle/line/arrow/polygon)
- [x] **Status:** merged (#79) — Square+Circle embedд'ятся; Line/Arrow/Polygon отложены
  (PDFium 146.x не экспонирует `/L`/`/Vertices`/`/LE` setter'ы) → подтрек **A2b** ниже
- **Файлы:** те же что A1 + tests
- **Acceptance:** `/Square` + `/Circle` в `/Annots` (✅); Line/Arrow/Polygon → A2b
- **Размер:** M
- **PR:** #79

### 3b. A2b — AnnotatedPdf: line/arrow/polygon через cos-level fallback
- [ ] **Status:** не начато (новый подтрек, выделен из A2)
- **Файлы:** `AnnotatedPdfExportService.cs` (cos-level `/L`/`/Vertices`/`/LE` запись поверх
  `FPDF_SaveAsCopy` output, либо path-object внутри pre-created form), tests
- **Acceptance:** Line/Arrow/Polygon → `/Annots` с корректным `/L`/`/Vertices`
- **Размер:** M
- **Заметка:** требует обхода ограничения PDFiumCore 146.x — возможно raw-dictionary
  пост-обработка. Низкий приоритет (round-trip через FDF/XFDF/JSON уже работает).
- **PR:** _tbd_

### 4. A3 — AnnotatedPdf: stamp с appearance stream
- [x] **Status:** merged (#80)
- **Файлы:** `AnnotatedPdfExportService.cs` (`AppendStampAppearance`), `PdfAnnotationSpec.cs`, `AnnotationToPdfSpec.cs` + tests
- **Acceptance:** `/Subtype /Stamp` + rect outline + centred label → Acrobat распознаёт как редактируемый stamp ✅
- **Размер:** M
- **PR:** #80

### 5. A5 — Image-stamp extension
- [ ] **Status:** не начато
- **Файлы:** `Annotation.cs` (Stamp factory overload), `AnnotationLayer.cs`, `AnnotatedPdfExportService.cs`, format round-trips
- **Acceptance:** Stamp с `ImagePath` рисуется как embedded PNG; round-trip через все 5 форматов
- **Размер:** M
- **Зависимость:** требует A3 (общий путь для stamp embedding)
- **Pre-work:** запустить `Plan` агента для развилки «StampSpec record vs. поле в Annotation»
- **PR:** _tbd_

### 6. L1 — MOBI loader
- [~] **Status:** draft PR опубликован
- **Файлы:** новый проект `src/Foliant.Engines.Mobi/` (`MobiDocumentLoader`, `MobiDocument`,
  `PalmDocCompression`, `MobiHtml`) + tests; DI в `AppHostBuilder`; sln/slnf
- **Acceptance:** открытие MOBI → `PageCount > 0` + text layer ✅ (in-box PalmDOC LZ77 разжиматель —
  библиотеки на NuGet не нашлось; HUFF/CDIC и AZW3/KF8 — Phase 2)
- **Размер:** M
- **Заметка:** File→Open filter для `.mobi` — в B3 (UI трек). Сейчас открывается через «All files».
- **PR:** _tbd_

### 7. B2 — Form-fill UI dialog
- [ ] **Status:** не начато
- **Файлы:** `FormFillDialog.xaml(.cs)`, `MainWindow.xaml(.cs)` (Tools → Fill Form…), Strings, FormFillViewModel
- **Acceptance:** диалог показывает поля формы PDF, пользователь правит → save → новый PDF на диск
- **Размер:** M
- **Блокирует:** не блокирует B1/B3, но идёт первым — разогревочный UI трек
- **PR:** _tbd_

### 8. B1 — Annotation tool palette toolbar
- [ ] **Status:** не начато
- **Файлы:** `MainWindow.xaml` (sidebar toolbar), 11 button-icons (vector), `DocumentTabViewModel.ActiveTool` state, drag-create handlers в `AnnotationLayer.cs`, Strings
- **Acceptance:** пользователь кликает кнопку → активный инструмент → drag по странице → создаётся аннотация нужного типа
- **Размер:** **L** (главный UI-кусок Phase 1 closure)
- **Pre-work:** `Plan` агент — спроектировать state-machine «текущий инструмент» + drag-create контракт
- **PR:** _tbd_

### 9. B3 — MOBI File-Open filter + Recent menu (после L1)
- [ ] **Status:** не начато
- **Файлы:** `MainWindow.xaml.cs` (filter), Strings
- **Acceptance:** «Open» dialog показывает MOBI; Recent помнит MOBI
- **Размер:** S
- **Зависимость:** L1 merged
- **PR:** _tbd_

### 10. D1 — OCR Windows smoke + offline-model packaging + CER гейт
- [ ] **Status:** не начато
- **Файлы:** `tools/fetch-natives.ps1` (или новый), CI workflow, новые tests
- **Acceptance:** S8 OCR закрывается ✅ — модели поставляются, CER ≤ target на эталонном корпусе
- **Размер:** M
- **Pre-work:** `Explore` агент — найти где сидят OCR-модели, какие .traineddata/.onnx ожидаются
- **PR:** _tbd_

### 11. D2 — Inno Setup `.iss` + GH Actions ISCC build
- [ ] **Status:** не начато
- **Файлы:** `installer/foliant.iss`, `.github/workflows/release.yml`
- **Acceptance:** S13 → ✅ (EV-sign остаётся внешним blocker'ом)
- **Размер:** M
- **Pre-work:** `Explore` агент — найти `Version`/`Authors`/RID/natives layout
- **PR:** _tbd_

### 12. D3 — Perf-bench harness
- [ ] **Status:** не начато
- **Файлы:** `tests/Foliant.Performance/` (расширить существующий)
- **Acceptance:** S1/S3/S7 имеют замеры; FTS5 ≤ 500ms / 10 docs зафиксировано
- **Размер:** M
- **PR:** _tbd_

### 13. E1 — Windows smoke pass + bug-fix sweep
- [ ] **Status:** не начато
- **Acceptance:** все 8+ PDFium-mutate треков проверены на реальном Windows; найденные баги — отдельные fix-PR'ы внутри этого трека
- **Размер:** S–M (зависит от количества находок)
- **PR:** _tbd_

---

## Итоговая оценка

- **13 запланированных PR** до Alpha DoD.
- **+2–3 PR запаса** на CodeQL fix-up'ы / ребейзы / Windows-only баги, найденные в E1.
- **Итого ~15 PR** до production-ready Alpha.

---

## Out-of-scope (Phase 2+, заморожено)

- Q-F5/F6/F7/F9 — inline-редактор с reflow, font matching, inpainting сканов
- Q-F8 — OCG layers editing
- Q-F22 — XFA с JS-движком (кандидат на veto)
- Q-F25 — full PAdES B+T validation (TSA timestamp + cert chain)
- Q-F26 — PAdES с TSA
- Q-F28 — LibreOffice плагин (out-of-process)
- Q-F30/F31/F32 — шифрование AES-256, permissions 8 флагов, redaction
- PDF/A, PDF/UA, PDF/X — спецстандарты

---

## Обновления

| Дата | Что | Кем |
|---|---|---|
| 2026-05-31 | Файл создан после merge #70–#76 | claude |
