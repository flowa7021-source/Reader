# Foliant — План реализации

> Дополнение к `PROJECT_BOARD.md`. Здесь — **как** строим, в каком порядке, с какими интерфейсами, тестами и acceptance-критериями. Решения «что строим» и «зачем» — в табло.

**Целевая аудитория документа:** разработчик(и) ядра + AI-агенты, которым нужно понимать, какой следующий конкретный шаг делать.

**Срок жизни документа:** перерабатывается в конце каждой фазы. Phase 0 и Phase 1 — детально по неделям. Phase 2–4 — каркас, детализация — после ретроспективы Phase 1.

---

## 0. Контракт качества кода (читать первым)

Все решения о коде проходят через эти фильтры. Нарушение — повод для review-блокера.

| Принцип | Конкретно |
|---|---|
| **KISS** | Простейшая реализация, удовлетворяющая acceptance. Никаких «на будущее». |
| **YAGNI** | Не делаем абстракцию, пока не нужно дважды. Параметризуем по факту. |
| **Один файл — одна ответственность** | Файл ≤ 300 строк. Метод ≤ 30 строк. Класс ≤ 7 публичных членов. Превышение — split. |
| **Без лишних слоёв** | Если нет двух реализаций интерфейса и тест не требует мока — нет интерфейса. |
| **Composition over inheritance** | Наследование запрещено вне `abstract base` для шаблона метода или WPF-контролов. |
| **Чистые зависимости** | `Foliant.Domain` не зависит ни от чего, кроме `Microsoft.Extensions.Logging.Abstractions`. |
| **async везде где IO** | Никаких `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` в продакшене. |
| **CancellationToken — обязателен** | Каждая `async`-операция > 50 мс принимает `CancellationToken`. |
| **Без комментариев-описаний** | Только WHY, не WHAT. Имя метода — документация. |
| **Без regions** | Нужны regions — значит файл слишком большой, разбей. |
| **Records для DTO** | `record` для immutable данных. `class` только если нужно поведение. |
| **File-scoped namespaces, primary constructors, pattern matching** | C# 12+ синтаксис без оговорок. |
| **NRT включён** | `<Nullable>enable</Nullable>` глобально. Каждый `?` — осознанное решение. |
| **TreatWarningsAsErrors** | Без исключений. Хочешь warning — `#pragma` с TODO+датой+автором. |
| **Один public class на файл** | Имя файла = имя типа. |

**Метрики, проверяемые в CI:**

- Цикломатическая сложность метода ≤ 10 (Roslyn analyzer `Roslynator`).
- Покрытие unit-тестами: Domain ≥ 90 %, Application/Services ≥ 80 %, Infrastructure ≥ 70 %, ViewModels ≥ 60 %, Views — не измеряем.
- `dotnet format --verify-no-changes` — обязателен.
- `dotnet build -warnaserror` — обязателен.

---

## 1. Структура решения (финальная для Phase 1)

```
Foliant.sln
├─ Directory.Build.props          # LangVersion=latest, Nullable=enable, TreatWarningsAsErrors=true,
│                                 #   AnalysisMode=All, EnforceCodeStyleInBuild=true
├─ Directory.Packages.props       # Central Package Management
├─ .editorconfig                  # 4 пробела, LF, UTF-8 BOM-less, file-scoped namespaces enforced
├─ src/
│  ├─ Foliant.Domain/             # Pure: IDocument, IPageRender, RenderOptions, DocumentMetadata,
│  │                              #   TextLayer, Annotation, value-объекты. Никаких зависимостей.
│  ├─ Foliant.Application/        # Сценарии: OpenDocumentUseCase, RenderPageUseCase, OcrPageUseCase.
│  │                              #   Зависит только от Domain. Без UI и Infrastructure.
│  ├─ Foliant.Infrastructure/     # Cache (5 слоёв), Serilog, Settings, EventStore, FileFingerprint,
│  │                              #   SqliteIndex, ProcessRunner (для out-of-process плагинов).
│  ├─ Foliant.Engines.Pdf/        # PDFium adapter → IDocument, IPageRender.
│  ├─ Foliant.Engines.Ocr/        # Tesseract adapter → IOcrEngine.
│  ├─ Foliant.Plugins.Contracts/  # Интерфейсы для MEF: IEnginePlugin, IConverterPlugin, ...
│  ├─ Foliant.UI/                 # WPF Views, custom controls, converters, behaviors.
│  ├─ Foliant.ViewModels/         # MVVM (CommunityToolkit.Mvvm). Без ссылок на UI/PresentationFramework.
│  └─ Foliant.App/                # Entry point. Composition root: DI + Serilog + main window.
├─ plugins/
│  └─ Foliant.Plugin.DjVu/        # Out-of-process wrapper над DjVuLibre CLI.
├─ tests/
│  ├─ Foliant.Domain.Tests/
│  ├─ Foliant.Application.Tests/
│  ├─ Foliant.Infrastructure.Tests/
│  ├─ Foliant.Engines.Pdf.Tests/  # Integration: реальный PDFium + эталонные PDF.
│  ├─ Foliant.Engines.Ocr.Tests/  # Integration: реальный Tesseract + эталонные сканы.
│  ├─ Foliant.ViewModels.Tests/
│  ├─ Foliant.E2E/                # WinAppDriver + эталонные сценарии (Phase 1 финал).
│  ├─ Foliant.Performance/        # BenchmarkDotNet.
│  └─ assets/                     # Эталонные PDF/DjVu/изображения (small, можно в git LFS).
├─ installer/
│  └─ Foliant.Installer.InnoSetup/
├─ tools/
│  ├─ fetch-natives.ps1           # Скачивает PDFium, Tesseract, проверяет SHA256.
│  └─ third-party/                # Пин-список версий + URL + SHA256.
├─ docs/                          # MkDocs Material источник.
└─ .github/
   ├─ workflows/
   │  ├─ ci.yml                   # build + test + format + analyzers (PR + push).
   │  ├─ codeql.yml               # security scan.
   │  ├─ release.yml              # tag → installer build + sign + GH Release.
   │  └─ perf.yml                 # nightly benchmark, regression alert.
   └─ pull_request_template.md
```

**Pro-репозиторий (закрытый, отдельный):** клонируется как submodule в `pro/` при наличии лицензии. Сборка `Foliant.App` детектит наличие и подключает Pro-проекты опционально через `<ItemGroup Condition="Exists('..\..\pro')">`.

---

## 2. Базовые контракты (Domain) — фиксируем сразу

Это «позвоночник» всего приложения. Меняется только при сильном основании, потому что миграции дороги.

```csharp
namespace Foliant.Domain;

public enum DocumentKind { Pdf, Djvu, Image, Epub, Fb2, Mobi }

public sealed record DocumentMetadata(
    string? Title, string? Author, string? Subject,
    DateTimeOffset? Created, DateTimeOffset? Modified,
    IReadOnlyDictionary<string, string> Custom);

public sealed record RenderOptions(
    double Zoom,                   // 1.0 = 72 dpi
    int? MaxWidthPx = null,
    int? MaxHeightPx = null,
    bool RenderAnnotations = true,
    RenderTheme Theme = RenderTheme.Original);   // Original | Dark | HighContrast

public enum RenderTheme { Original, Dark, HighContrast }

public sealed record PageSize(double WidthPt, double HeightPt);

public interface IPageRender : IDisposable
{
    int WidthPx { get; }
    int HeightPx { get; }
    ReadOnlyMemory<byte> Bgra32 { get; }   // pre-multiplied BGRA, ready для WriteableBitmap
    int Stride { get; }
}

public sealed record TextRun(string Text, double X, double Y, double W, double H);
public sealed record TextLayer(int PageIndex, IReadOnlyList<TextRun> Runs);

public interface IDocument : IAsyncDisposable
{
    DocumentKind Kind { get; }
    int PageCount { get; }
    DocumentMetadata Metadata { get; }
    PageSize GetPageSize(int pageIndex);
    Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct);
    Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct);
    IDocumentEditor? GetEditor();           // null для read-only
    IFormController? GetForms();            // null если форм нет
    ISignatureController? GetSignatures();  // null если подписей нет
}

public interface IDocumentLoader
{
    bool CanLoad(string path);
    Task<IDocument> LoadAsync(string path, CancellationToken ct);
}
```

**Правила работы с этими типами:**

- Все возвращаемые `IPageRender` — disposable, владелец — caller. Кэш владеет «копией», UI — «арендатором».
- `Bgra32` — `ReadOnlyMemory<byte>`, чтобы `WriteableBitmap.WritePixels` работал zero-copy через `MemoryMarshal`.
- `TextLayer` — value object, легко (де)сериализуется в JSON.gz для disk cache.
- Никаких событий и наблюдателей в `IDocument`. Подписка на «документ изменился» — на уровне ViewModel поверх `IDocumentEditor`.

---

## 3. Phase 0 — подготовка (4–6 недель)

Цель фазы: к её концу разработчик может за 5 минут открыть solution и за 1 минуту собрать MVP-вьюер для одной PDF.

### Неделя 1 — юридика и закупки (параллельно с кодом)

| Задача | Кто | Output |
|---|---|---|
| Поиск ТЗ Foliant в Роспатенте (МКТУ-9, -42) | Юрист | Заключение: свободно / коллизия / нужна модификация |
| Проверка US TM (USPTO TESS) и EU (EUIPO eSearch) | Юрист | Заключение |
| Проверка коллизии с OSS-Foliant (foliant-docs) | Сам | Risk note в `docs/BRAND.md` |
| Регистрация доменов (foliant.app/.io/.ru или альтернатива) | Сам | Записи в DNS-провайдере |
| Заявка на EV или Standard Code Signing certificate | Сам | Сертификат в HSM/токене (приходит 1–6 недель) |
| Закладка бюджета на CI runners (Windows-only, ≈ $40–80/мес) | Сам | Решение «свои runners vs GitHub-hosted» |

**Решение перед стартом недели 2:** утверждено имя бренда. Если коллизия — переключение на запасной (Folio / Verso) **до** генерации логотипа и закупки сертификата на CN.

### Неделя 2 — репозиторий и CI

Acceptance: пустой PR проходит CI: build + format + analyzers + zero tests = green за < 3 минут.

- `git init`, добавить `.gitignore` (Visual Studio template + `pro/` + `*.tessdata` + `Cache/` + `Autosave/`).
- `Directory.Build.props` (см. Раздел 1).
- `Directory.Packages.props` — Central Package Management включён.
- `.editorconfig` с включёнными `IDE0161` (file-scoped namespaces), `IDE0040` (accessibility modifiers), `IDE0011` (always braces), `CA*` правила.
- `.github/workflows/ci.yml`: matrix `windows-latest`, `dotnet --version` 10, `dotnet restore --locked-mode`, `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test --collect:"XPlat Code Coverage"`.
- `.github/workflows/codeql.yml`: weekly + on-PR.
- `pull_request_template.md`: чеклист (тесты, обновлён CHANGELOG, скриншот для UI, перфиметрики если затронуты горячие пути).
- Branch protection: `main` — нельзя push без PR, нужен зелёный CI и 1 review.
- Pre-commit hook (опц., через [Husky.Net](https://alirezanet.github.io/Husky.Net/)): `dotnet format` + run unit tests на изменённых проектах.

### Неделя 3 — скелет solution и DI

Acceptance: `Foliant.App` стартует, открывает пустое окно «Foliant», пишет лог «App started» в `%LOCALAPPDATA%\Foliant\Logs\foliant-{date}.log`, корректно завершается.

- Создать все 9 проектов из Раздела 1 (без кода, только `Class1.cs` placeholder).
- Composition root в `Foliant.App/Program.cs`:
  ```csharp
  var host = Host.CreateApplicationBuilder(args);
  host.Logging.ClearProviders();
  host.Services.AddSerilog((sp, lc) => lc
      .MinimumLevel.Information()
      .WriteTo.File(Path.Combine(LocalAppData("Foliant", "Logs"), "foliant-.log"),
                    rollingInterval: RollingInterval.Day));
  host.Services.AddSingleton<IFileFingerprint, FileFingerprint>();
  host.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
  // engines/loaders подключаются в Phase 1 спринт 1
  var app = host.Build();
  ```
- `Foliant.UI/MainWindow.xaml` — пустое `Window` с заголовком из `IStringLocalizer`.
- Smoke-тест: `Foliant.App.Tests` запускает приложение в тестовом режиме (`--smoke`) и проверяет, что лог-файл создан и содержит «App started».

### Неделя 4–5 — PDFium-прототип (throw-away)

Acceptance: окно открывает выбранный PDF, скроллит, рендерит текущую видимую страницу. Качество кода — **черновик, выкидываем**. Цель — получить уверенность, что библиотечный путь работает на Windows.

- Подключить [PDFiumCore](https://www.nuget.org/packages/PDFiumCore) (NuGet, BSD-3, нативка внутри).
- В `Foliant.Engines.Pdf` написать **ровно** один класс `PdfDocument : IDocument` со всеми методами, бросающими `NotImplementedException`, кроме `RenderPageAsync` и `PageCount`.
- В `Foliant.UI` — `PdfPreviewWindow` с одной `Image`, обновляющейся по `PreviewKeyDown` (PgUp/PgDn).
- Тест на 3 эталонных файлах из `tests/assets/`: 10-стр текст, 200-стр скан, 5-стр с кириллицей.

### Неделя 6 — стратегические решения

Решения принимаются и фиксируются в `PROJECT_BOARD.md` (история изменений) до старта Phase 1:

1. Reality-check стратегия: A / B / C (Раздел 0 табло).
2. Судьба XFA: оставляем в Phase 4 / вычёркиваем.
3. Поиск 2-го разработчика: да / нет / пока нет.
4. Code signing получен или есть план получения к концу Phase 1.

---

## 4. Phase 1 — Альфа (план по 2-недельным спринтам)

**Длительность фазы:** 12–13 спринтов × 2 недели = 24–26 недель.
**Definition of Done спринта:** мердж в `main`, зелёный CI, обновлён `CHANGELOG.md`, обновлён `docs/`, ручной smoke по чек-листу из спринта пройден.

| № | Длит. | Тема | Главный артефакт | Acceptance |
|---|---|---|---|---|
| **S1** | 2 нед | Domain + IDocumentLoader + чистый PDFium | `IDocument`, `PdfDocument`, `PdfDocumentLoader` без хаков | Открыть 50 эталонных PDF, прочитать metadata + page count, отрендерить случайную страницу. 95-й перцентиль рендера ≤ 100 мс на 1080p странице. |
| **S2** | 2 нед | UI shell: MDI tabs + Per-Monitor V2 + темы (light/dark/HC) | `Foliant.UI` с `TabControl`, `ThemeManager`, hot-swap темы | Открыть 5 PDF в табах, переключить тему — все страницы перерисовываются без артефактов. |
| **S3** | 2 нед | In-memory кэш (слои 1–3): page bitmap LRU + thumb + text/structure | `MemoryPageCache`, `ThumbnailCache`, `TextStructureCache` | Скролл туда-сюда по 200-стр документу: повторный рендер страницы ≤ 5 мс. RAM ≤ настроенного лимита. |
| **S4** | 2 нед | Persistent disk-кэш (слой 4) + fingerprint + invalidation | `DiskCache`, `FileFingerprint`, `CacheKey` | Закрыть/открыть документ повторно: страницы из кэша ≤ 20 мс. Изменение файла → автоинвалидация. |
| **S5** | 2 нед | Локализация RU/EN + Settings dialog + Recents | `IStringLocalizer<T>` + `Foliant.UI/Resources/*.resx` + `SettingsWindow` + `RecentsService` | Полное переключение UI RU↔EN без перезапуска. Settings сохраняются. Recents показывает последние 20 файлов. |
| **S6** | 2 нед | Текстовый слой + поиск in-document (Ctrl+F) | `PdfDocument.GetTextLayerAsync`, `SearchService`, sidebar результатов | На 500-стр документе поиск слова, есть в 100 местах: ≤ 1 с холодно, ≤ 100 мс тепло. |
| **S7** | 2 нед | Persistent search index (слой 5) — SQLite FTS5 | `SqliteFtsIndex`, фоновая индексация | Поиск по 10 индексированным документам: ≤ 500 мс. |
| **S8** | 2 нед | OCR pipeline: Tesseract LSTM (рус+eng) | `TesseractOcrEngine`, `OcrPipeline` (deskew → despeckle → OCR → text-layer attach) | OCR 10-стр скана ≤ 30 с. Поиск по результату работает. Кэш OCR (слой 4) переиспользуется. |
| **S9** | 2 нед | Out-of-process DjVu плагин | `Foliant.Plugin.DjVu` (CLI wrapper над `ddjvu`/`djvused`), `DjvuDocument` | Открыть DjVu, отрендерить страницы, запустить OCR, получить текстовый слой. |
| **S10** | 2 нед | Аннотации базовые (highlight / sticky note / freehand) + рендер поверх страницы | `AnnotationLayer`, `IAnnotationService`, persist через PdfPig | Поставить highlight, закрыть, открыть — аннотация на месте. |
| **S11** | 2 нед | Page management: rotate / delete / insert / reorder + thumbs-strip drag | `PageManagementService` (через QPDF), `ThumbStrip` control | Реорганизовать 100-стр документ, сохранить, открыть в Acrobat — структура корректна. |
| **S12** | 2 нед | Простой text editor (без reflow): replace single line + add text box | `SimpleTextEditor`, базовый PDF→DOCX (текст по позициям через OpenXml) | Заменить 5 строк, экспорт в DOCX, открыть в Word — текст распознан. |
| **S13** | 2 нед | License manager + 30-day триал + anti-tamper + Inno Setup installer + EV-подпись | `LicenseManager`, `TrialAntiTamper`, `installer/Foliant.iss`, signed `Foliant-Setup.exe` | Чистая Win10 21H2 VM: установить → запустить → триал активен 30 дней → ввести лицензию → активировано. Uninstall чистый. |

### 4.1. Заморозка scope для альфы

Что **точно НЕ** делаем в Phase 1 (даже если успели):

- Inline-редактор с reflow.
- XFA, PAdES, шифрование PDF, redaction.
- LibreOffice плагин.
- PDF/A валидация.
- Watermarks / headers / footers.
- Crash reporter (только локальные логи).
- Auto-save + event-sourced undo (Phase 2).
- ARM64.

Это сохраняет дисциплину и позволяет уложиться в 6 месяцев.

### 4.2. Контроль здоровья проекта

После каждых 2 спринтов (раз в месяц):

- Прогон полного performance-сьюта `tests/Foliant.Performance/`.
- Сравнение с baseline (хранится в `tests/Foliant.Performance/baseline.json`). Регрессия > 15 % — **блокер релиза следующего спринта**.
- Smoke-чек на 3 «золотых» документах (текст, скан, сложная вёрстка) на чистой Win10 VM.
- Демо для самого себя: показать функционал «как пользователь». Записать видео ≤ 5 мин в `docs/demos/`.

---

## 5. Подсистемы — детально

Здесь — конкретика по тем местам, где есть нетривиальные решения. Раздел сжат, но каждый абзац — про реализацию, не про стратегию.

### 5.1. Кэш (5 слоёв)

**Ключ кэша** — единый `record CacheKey`:

```csharp
public sealed record CacheKey(
    string DocFingerprint,   // sha256(first64KB || size || mtime).ToHex()
    int    PageIndex,
    int    EngineVersion,    // semantic version пакета engine
    int    ZoomBucket,       // round(zoom * 100 / 25) * 25
    int    Flags)            // битовая маска: AnnotationsOn=1, Theme.Dark=2, Theme.HC=4, ...
{
    public string ToFileName() => $"{DocFingerprint}_{PageIndex}_{EngineVersion}_{ZoomBucket}_{Flags}.bin";
}
```

**Слои:**

1. `MemoryPageCache` — `LruCache<CacheKey, IPageRender>` с тотальным capacity в байтах. Sticky-окно ±5 страниц от текущей не эвиктится.
2. `ThumbnailCache` — `Dictionary<int, BitmapSource>` per document; живёт пока документ открыт.
3. `TextStructureCache` — `Dictionary<int, TextLayer>` + `Lazy<DocumentOutline>` per document.
4. `DiskCache` — `IDiskCache` с операциями `TryGetAsync(key)`, `PutAsync(key, bytes)`, `EvictLruAsync(targetBytes)`. Реализация: файлы в `pages/`, метаданные в `metadata.db` (SQLite, одна таблица `entries(key TEXT PRIMARY KEY, size INT, last_access INT, doc_fp TEXT)`).
5. `SqliteFtsIndex` — таблица `documents`, виртуальная таблица `pages_fts` (FTS5), фоновый indexer запускается при первом открытии документа в IDLE-thread.

**Eviction:** один-единственный фоновый `CacheJanitor : BackgroundService`, тикает раз в 30 с, держит размер ниже soft-limit (90 % hard-limit). Memory pressure (`QueryMemoryResourceNotification` через P/Invoke на `kernel32`) → внеплановый eviction RAM-кэшей до 50 %.

**Тесты (обязательно):**

- Unit для `LruCache`: добавление, eviction по capacity, sticky-окно.
- Unit для `CacheKey.ToFileName`: стабильность между процессами (snapshot test).
- Integration для `DiskCache`: concurrent put/get, корректность eviction по LRU-времени, восстановление после kill процесса (метаданные транзакционны).
- Property-test (FsCheck) для `FileFingerprint`: тот же файл → тот же fingerprint; изменение байта → разный fingerprint.

### 5.2. PDFium adapter (`Foliant.Engines.Pdf`)

- Поверх `PDFiumCore`. Один статический `PdfiumRuntime` инициализирует/освобождает нативку.
- Вся работа с native-указателями инкапсулирована в `internal sealed class PdfiumPageHandle : SafeHandle`.
- `RenderPageAsync` запускает рендер на dedicated `STA`-неподобном пуле (на самом деле — `LimitedConcurrencyTaskScheduler` с `Environment.ProcessorCount - 1`), потому что PDFium не любит параллельные вызовы для одного документа: per-document `SemaphoreSlim(1,1)`.
- Отдельный класс `PdfTextExtractor` для текстового слоя (`FPDFText_*` функции).
- Отказоустойчивость: при `AccessViolationException` (редко, но бывает на битых PDF) — мы запускаем PDFium **в дочернем процессе** только для проблемных файлов (опц. фоллбэк, Phase 2). В Phase 1 — просто ловим и показываем «Файл повреждён».

### 5.3. OCR pipeline

```
RawBitmap → Deskew (Hough) → Despeckle (median 3×3) → Binarize (Otsu)
         → Tesseract (LSTM) → PostProcess (склейка дефисов на переносах)
         → TextLayer attached to page
         → Cache write (JSON.gz)
```

- Препроцессинг — через `ImageSharp` (не нужно подключать OpenCV в Phase 1; добавим в Phase 3, когда нужен inpainting).
- Tesseract — пакет [Tesseract](https://www.nuget.org/packages/Tesseract) (Apache-2.0). Tessdata-LSTM модели грузятся из `native/tesseract/tessdata`.
- Параллелизм: страница за страницей, не больше `min(4, CPU/2)` одновременно.
- UI: progress per page + cancel в любой момент.
- Тесты: 5 эталонных сканов с известным «правильным» текстом → CER (character error rate) ≤ 2 % для рус, ≤ 1 % для eng. Регрессия CER > +0.5 п.п. — блокер.

### 5.4. DjVu плагин (out-of-process)

- Отдельный installer (`Foliant.Plugin.DjVu.Setup.exe`, ~5 МБ) кладёт `ddjvu.exe` + `djvused.exe` в `%ProgramFiles%\Foliant\Plugins\DjVu\bin\` + регистрирует в `HKCU\Software\Foliant\Plugins\DjVu = {InstallPath}`.
- `Foliant.Plugin.DjVu` экспортирует `IDocumentLoader` и `IDocument` через MEF.
- Каждый рендер страницы — `Process.Start("ddjvu", "-format=ppm -page=N input.djvu -")`, чтение stdout как PPM, парсинг в BGRA32. Overhead ≈ 50–150 мс.
- Тесты: integration с 3 эталонными DjVu (book, scan, indirect).

### 5.5. Editor (поэтапно)

| Этап | Что умеет | Where |
|---|---|---|
| **E1 (Phase 1, S12)** | Заменить целую строку on-the-spot, добавить новый текстовый блок | `Foliant.App` (open) |
| **E2 (Phase 2)** | Reflow внутри одного абзаца (`/Tj` / `/TJ` regen с теми же шрифтом и размером) | `Foliant.Pro.AdvancedEditor` (closed) |
| **E3 (Phase 3)** | Полный reflow + font matching (matching по metrics через FontMatching service) | `Foliant.Pro.AdvancedEditor` |
| **E4 (Phase 3)** | Заплатки на сканах: OpenCV inpainting + новый текст сверху | `Foliant.Pro.AdvancedEditor` |

**Ключевое архитектурное решение** для редактора: **command-pattern + event store**. Каждое действие пользователя — `IDocumentCommand` с `Apply` / `Invert`. Сохраняются в `EventStore` (append-only JSONL в `Autosave/{docId}/events.jsonl`). Это сразу даёт:
- Undo/Redo (Q-A11) бесплатно.
- Auto-save: persist каждого события мгновенно.
- Crash recovery: при старте проверяем все `Autosave/*` директории, предлагаем восстановить.
- Базу для будущей коллаборации (Раздел 12.4 табло).

### 5.6. Аннотации

- Хранение в самом PDF через `PdfPig` (Apache-2.0): он лучше для модификации структуры, чем PDFium.
- Слои: `AnnotationLayer` рисуется поверх рендера PDFium как WPF `Canvas` с custom-нарисованными элементами. Hit-testing — через `VisualTreeHelper.HitTest`.
- FDF / XFDF — Phase 2.

### 5.7. License + триал

- Лицензия: JSON `{ user, sku, expiresAt, features[] }` + ECDSA-P256 подпись, хранится в `%APPDATA%\Foliant\license.key`, шифрование DPAPI (Current User scope).
- Публичный ключ — захардкожен в `LicenseManager`. Приватный — у разработчика, никогда не в репо.
- Триал: при первом запуске пишем `trial.dat` (DPAPI-зашифрованный JSON со стартовой датой и nonce) **И** дублирующее значение в `HKCU\Software\Foliant\Trial\StartedAt` **И** хэш в `Autosave/.trial-marker`. Любая разсинхронизация — триал считается истёкшим. Перевод системного времени назад — детектится через сохранение «макс. виденного времени».
- Тесты unit: подделка любого из трёх хранилищ → `IsTrialValid()` = false.

### 5.8. Settings и конфигурация

- `JsonSettingsStore` поверх `System.Text.Json` с source-generated context (zero-reflection).
- Schema-versioned (`SettingsV1`, `SettingsV2`, миграции в `SettingsMigrator`).
- Hot-reload через `FileSystemWatcher` для удобства разработки (можно править JSON руками).

---

## 6. Стратегия тестирования

### 6.1. Категории и инструменты

| Категория | Инструмент | Где запускается | Скорость |
|---|---|---|---|
| Unit | xUnit + FluentAssertions + NSubstitute | Каждый PR | < 10 с весь сьют |
| Property-based | FsCheck | Каждый PR | < 30 с |
| Integration (с реальным движком) | xUnit + `Foliant.Engines.*` + `tests/assets/` | Каждый PR | < 2 мин |
| Snapshot (рендер, сериализация) | Verify.Xunit | Каждый PR | < 30 с |
| Performance | BenchmarkDotNet | Nightly + on-demand | 5–15 мин |
| E2E UI | Appium + WinAppDriver | Pre-release | 5–10 мин |
| Manual smoke | Markdown-чеклист в `tests/manual/RELEASE_SMOKE.md` | Pre-release | 30 мин |

### 6.2. Целевое покрытие

| Слой | Покрытие unit | Обоснование |
|---|---|---|
| Domain | ≥ 90 % | Чистая логика, дёшево покрыть. |
| Application | ≥ 80 % | Сценарии, мокаем порты. |
| Infrastructure | ≥ 70 % | Часть — IO, тестируем integration. |
| Engines.* | Integration ≥ 80 % сценариев | Unit имеет смысл только для адаптерных слоёв. |
| ViewModels | ≥ 60 % | Команды, состояния. Без View. |
| UI Views | 0 % | Ловится E2E + ручным smoke. |

CI **рушит** PR при падении покрытия > 2 п.п. в проектах Domain/Application/Infrastructure.

### 6.3. Эталонный набор активов (`tests/assets/`)

Минимальный «золотой» набор, без которого нельзя релизить:

- 3 PDF (текст, скан, mixed) — лицензия CC0 / public domain.
- 2 PDF с кириллицей.
- 1 PDF с RTL (арабский).
- 1 PDF с CJK.
- 1 PDF с формами AcroForm.
- 1 PDF с электронной подписью.
- 2 DjVu (book / scan).
- 3 повреждённых PDF (для тестов robustness).

Все хранятся в Git LFS, файл < 5 МБ каждый. Источники документируем в `tests/assets/README.md`.

### 6.4. Performance baseline

Файл `tests/Foliant.Performance/baseline.json`:

```json
{
  "OpenPdf500Pages":     { "p50_ms": 250, "p95_ms": 500 },
  "RenderPage1080p_Cold":{ "p50_ms": 80,  "p95_ms": 150 },
  "RenderPage1080p_Warm":{ "p50_ms": 3,   "p95_ms": 8   },
  "SearchAcross10kPages":{ "p50_ms": 600, "p95_ms": 1500 },
  "OcrPageRus":          { "p50_ms": 1500,"p95_ms": 3000 },
  "AppColdStart":        { "p50_ms": 800, "p95_ms": 1500 }
}
```

Регрессия > 15 % p95 в любом из бенчмарков на nightly → Issue с лейблом `perf-regression`, блокирует следующий релиз.

### 6.5. Соглашения по unit-тестам

- Имя теста: `MethodName_StateUnderTest_ExpectedBehavior` (классика). В русских названиях допустимо `Method_Когда_X_То_Y`.
- Arrange / Act / Assert — пустыми строками.
- Один тест — одно утверждение по сути (можно несколько `Should()` про один и тот же объект).
- `[Theory]` + `[InlineData]` для параметризации, не циклы.
- Никаких `Thread.Sleep`. Только `await Task.Delay` с очень короткими интервалами + `[Trait("Category","Slow")]`.
- Никаких глобальных моков. Каждый тест строит свои.
- Тестовые данные — через builder-паттерн (`PdfDocumentBuilder.New().With2Pages().Build()`), без магических литералов.

---

## 7. CI / CD

### 7.1. CI (`ci.yml`)

Trigger: `pull_request` в `main`, `push` в `main`.

```yaml
jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with: { lfs: true }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: pwsh tools/fetch-natives.ps1
      - run: dotnet restore --locked-mode
      - run: dotnet format --verify-no-changes
      - run: dotnet build -c Release -warnaserror
      - run: dotnet test -c Release --collect:"XPlat Code Coverage"
                --filter "Category!=Slow&Category!=E2E"
      - uses: codecov/codecov-action@v4
      - run: dotnet run --project tests/Foliant.Performance -- --filter '*' --quick
        if: github.event_name == 'pull_request'
        # quick mode только убеждается, что бенчмарки запускаются и не падают
```

### 7.2. CodeQL (`codeql.yml`)

- Языки: csharp.
- Расписание: weekly + on PR.

### 7.3. Release (`release.yml`)

Trigger: tag `v*.*.*`.

```text
1. Checkout (LFS).
2. Restore + build (Release, AnyCPU).
3. dotnet test (full сьют, включая Slow).
4. dotnet publish Foliant.App -c Release -r win-x64 --self-contained.
5. Sign Foliant.exe + ключевые DLL signtool.exe (HSM/токен с EV cert).
6. Сборка инсталляторов (3 tier'a) iscc.exe.
7. Sign installers signtool.exe.
8. SHA256SUMS + GPG signature.
9. Создать GH Release с changelog (читаем из CHANGELOG.md по тегу).
10. Аплоад артефактов: 3 .exe + SHA256SUMS + .asc.
```

### 7.4. Nightly perf (`perf.yml`)

- Расписание: 02:00 UTC.
- Прогон полного performance сьюта.
- Сравнение с `baseline.json` через утилиту `tools/perf-compare/`.
- Регрессия → автоматическое создание Issue с лейблом `perf-regression`.

### 7.5. Branch hygiene

- `main` — всегда зелёный, защищён.
- Feature branches: `feat/<sprint>-<short>`, `fix/<short>`, `chore/<short>`.
- PR ≤ 400 LOC изменений (исключения — добавление тестовых ассетов и автогенерируемые файлы).
- Squash merge как дефолт. Один PR — один логический change.
- Conventional Commits: `feat: …`, `fix: …`, `perf: …`, `refactor: …`, `docs: …`, `test: …`, `chore: …`.

---

## 8. Phase 2 — Бета (каркас, перепланируется после Phase 1)

Длительность: 6–9 месяцев. 12–18 спринтов.

| Поток | Содержит | Прим. чел-мес |
|---|---|---|
| Inline-редактор reflow в абзаце | `Foliant.Pro.AdvancedEditor` E2 | 2–3 |
| Полный набор аннотаций + FDF/XFDF | расширение `IAnnotationService` | 1–1.5 |
| Все режимы просмотра + RTL/CJK (просмотр) | `Foliant.UI` PageSurface | 1 |
| Page management полный + watermarks/headers/crop | `PageManagementService` v2 | 1.5 |
| PDF/A валидация + создание (через veraPDF) | `Foliant.Pro.PdfA` | 3–4 |
| Цифровые подписи X.509 + PAdES B+T | `Foliant.Pro.Signatures` | 3–4.5 |
| Шифрование AES-256 + permissions | `Foliant.Pro.Crypto` | 1.5 |
| Multi-tier инсталляторы (Standard/Full) | installer/ | 0.5 |
| Crash reporting (opt-in) | `Foliant.Infrastructure.CrashReporter` | 0.5 |
| Auto-save + event-sourced undo | `Foliant.Infrastructure.EventStore` | 1.5 |

Ключевые риски Phase 2 — перенос Pro-кода в закрытый репозиторий и поддержка двух pipeline (open + open+pro). Решение: **один и тот же solution**, но Pro-проекты тянутся как git submodule в `pro/` и подключаются через `<ItemGroup Condition>` в `Foliant.App.csproj`. CI закрытого репо запускает `Foliant.App` со всеми Pro подключёнными; CI публичного — без. Так избегаем дублирования.

## 9. Phase 3 — 1.0 (каркас)

Длительность: 6–9 месяцев. Главные потоки:

- **Inline-редактор полный reflow + font matching** (`Foliant.Pro.AdvancedEditor` E3) — самая дорогая фича.
- **Inpainting сканов** (`OpenCV` через OpenCvSharp4) — E4.
- **Редактирование DjVu** (плагин: запись текстового слоя через `djvused`).
- **LibreOffice плагин** для high-fidelity конвертации.
- **Все конвертации** (EPUB/CSV/изображения + обратная).
- **Batch processing** (`Foliant.Pro.Batch`).
- **Полное redaction** (`Foliant.Pro.Redaction`).
- **OCG** полная поддержка.
- **Form data export** (FDF + XFDF + JSON).

## 10. Phase 4 — 1.5+ (после ретроспективы 1.0)

- PDF/UA + PDF/X (через LittleCMS).
- XFA с JS — **только если решено сохранить** в Phase 0.
- ГОСТ-подписи (`Foliant.Pro.GostSignatures`).
- ARM64.
- Редактирование RTL/CJK (Bidi).

## 11. Привязка плана к рискам из табло

| Риск (раздел 11 табло) | Митигация в этом плане |
|---|---|
| Бюджет на code signing не определён | Phase 0 / неделя 1 — закупка сертификата как **критический gating-чекпоинт**. До получения — нет S13 (installer + sign). |
| Срок 6 мес до альфы нереалистичен | Раздел 4.1 — **жёсткий заморозочный список** того, чего НЕ делаем в Phase 1. |
| Inline-редактор в 2–3× дольше прогноза | Раздел 5.5 — **четыре этапа E1–E4**, каждый — самостоятельный релиз ценности. Ранний останов возможен после любого этапа. |
| XFA не оправдает инвестиций | Раздел 3 / неделя 6 — **gate-решение** до Phase 1. |
| Качество PDF→DOCX недостаточно | Раздел 5.5 / Phase 3 — **гибрид**: LibreOffice + native конвертер; пользователь выбирает per-document. |
| Без ГОСТ продукт неприемлем для нотариусов РФ | Принято: нотариусы — не primary в Phase 1–3. ГОСТ — Phase 4. |
| AV false positives | Раздел 7.3 — sign **с самого начала**; submit билдов в Microsoft + ESET / Kaspersky / Dr.Web для whitelisting. |
| Утечка через кэш | Раздел 5.1 — NTFS ACL по умолчанию; защищённые PDF не кэшируем; опция DPAPI cache. |
| Performance regression | Раздел 6.4 — baseline + nightly perf + 15 % p95 как hard gate. |
| Конфликт бренда | Phase 0 / неделя 1 — gate-проверка. |
| AI-агенты не дают ускорения | Раздел 4.2 — после каждых 2 спринтов проверяем темп; пересмотр scope при отставании > 1 спринта. |

---

## 12. Definition of Done — Phase 1 (Альфа)

Релиз 0.1 «Альфа» считается готовым, когда:

1. ✅ Установка из `Foliant-Setup-0.1.exe` (signed) на чистой Win10 21H2 и Win11 — без warnings SmartScreen для подписанного билда.
2. ✅ Все 13 спринтов S1–S13 закрыты, мердж в `main`, тег `v0.1.0`.
3. ✅ CI зелёный.
4. ✅ Покрытие unit-тестами не ниже целей раздела 6.2.
5. ✅ Performance baseline соблюдён.
6. ✅ Manual smoke по `tests/manual/RELEASE_SMOKE.md` пройден.
7. ✅ `docs/` обновлён, опубликован MkDocs Material сайт.
8. ✅ EULA + privacy policy + третьи лицензии (NOTICE.md) — в инсталляторе и в `%ProgramFiles%\Foliant\Licenses\`.
9. ✅ License manager и триал работают (E2E проверено на VM с переводом времени и без).
10. ✅ В Recents помещаются 20 последних, работает «Удалить и кэш».

Если что-то одно из 1–10 — не выполнено, релиз сдвигается.

---

## 13. Что положить в репо в самом начале (вместе с этим планом)

1. `IMPLEMENTATION_PLAN.md` — этот файл.
2. `CHANGELOG.md` (Keep a Changelog format) с пустой секцией `[Unreleased]`.
3. `CONTRIBUTING.md` — контракт качества кода (раздел 0) + как запускать тесты + Conventional Commits.
4. `CODE_OF_CONDUCT.md` — стандартный Contributor Covenant.
5. `SECURITY.md` — куда репортить уязвимости (private GH advisory).
6. `LICENSE` — MIT для open-core.
7. `NOTICE.md` — пустой шаблон для третьих лицензий, заполняется по мере добавления зависимостей.
8. `README.md` — ссылки на табло, план, build instructions (build instructions появятся после Phase 0 / неделя 3).

---

*Документ — рабочий. Любое изменение фиксируется через PR с лейблом `plan-update` и кратким разделом «Что и зачем поменяли» в описании PR.*

