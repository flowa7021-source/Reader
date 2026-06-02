# Foliant — Phase 1 (Alpha) DoD: оставшиеся работы

**Создан:** 2026-05-31, после merge PR'ов #70–#76.
**Обновлён:** 2026-06-01, после merge PR'ов #77–#91 (А1–A5, B1a–B1d, B2/B3, L1, D3).
**Базовый контракт:** один PR → draft → merge → следующий от свежего main. Параллелить только pure-application/engine треки, не трогающие `MainWindow.xaml(.cs)` и `Strings*.resx`.

---

## Статус-снимок (после #91 на main)

- **Phase 1 (Alpha):** 12/13 спринтов ✅, 1/13 🟡 (S8 OCR — инфраструктура готова, golden-scan корпус остаётся за Windows-стендом).
- **Q-F фичи:** 23 ✅, 1 🟡 (Q-F18 image-stamps — UX в #93 in-flight), 9 заморожены Phase 2+.
- **Тесты:** Domain 218 / Application 362 / ViewModels 583 (target gates D90/A80/I70/V60 держатся: 99.7 / 85.7 / 80.8 / 88.4).
- **Перф-баseline:** S1/S3 (`Render_Single_Page_At_100_Percent`, `Open_100_Page_Pdf`) добавлены, гейты ≤500 ms / ≤2 s.
- **Inflight:** #92 (A5b: image-stamp PDF embed), #93 (B1e: image-stamp UX).

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
- [~] **Status:** in-flight PR #92
- **Файлы:** `PdfAnnotationSpec.cs`, `AnnotationToPdfSpec.cs`, `AnnotatedPdfExportService.cs`
- **Acceptance:** `Annotation.ImageStamp(…)` → PDF `/Subtype /Stamp` с embedded image-object (FPDFPageObjNewImageObj + SetBitmap).

### B1e — image-stamp UX
- [~] **Status:** in-flight PR #93
- **Файлы:** `DocumentTabViewModel.Annotations.cs`, `DocumentTabViewModel.Tools.cs`, `MainWindow.xaml(.cs)`, Strings
- **Acceptance:** Toolbar «Pick image…» / «Clear image» при активном Stamp → создаются image-stamps.

### A2b — line/arrow/polygon native PDF embed
- [ ] **Status:** не начато (низкий приоритет)
- **Что:** PDFium 146.x не экспонирует setter'ы для `/L`/`/Vertices`/`/LE`. Нужен cos-level fallback —
  пост-процессинг PDF-байтов после `FPDF_SaveAsCopy` через PdfPig или raw cos-writer. Сложно.
- **Acceptance:** Line/Arrow/Polygon → `/Annots` с корректным `/L`/`/Vertices`.
- **Заметка:** Round-trip через FDF/XFDF/JSON уже работает; UI render тоже. Phase 1 без этого
  закрывается — Phase 2.

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

- **22 PR закрыто** в этой сессии. Annotation-палитра feature-complete, Q-F1 = 5/5, Q-F17 = 11/11,
  Q-F24/Q-F25 имеют UI, Q-F18 image-stamps в флайте.
- **2 PR в полёте**: #92 + #93 — закрывают Q-F18 image-stamps end-to-end.
- **Остаток до Alpha DoD:** A2b (low-pri, можно отложить в Phase 2), D1-corpus, E1 (Windows). Из
  них только E1 truly required для DoD-аттестации.

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
| 2026-06-01 | Refresh: добавлены 22 закрытых трека, отмечены подтверждённые D1/D2, обновлён список оставшихся работ | claude |
| 2026-06-01 | A5c → ✅ done: ImagePath round-trip через XFDF/FDF (углы: non-stamp, Unicode, спецсимволы) | claude |
