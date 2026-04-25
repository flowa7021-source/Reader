# Architecture

> Этот документ — **архитектура верхнего уровня**. Подробности — в `PROJECT_BOARD.md` (что и зачем) и `IMPLEMENTATION_PLAN.md` (как, по спринтам).
> Документ **обязателен к обновлению** в каждом PR, который меняет границы слоёв или добавляет новый формат документа.

## 1. Слои

```
┌──────────────────────────────────────────────────────────┐
│                      Foliant.UI (WPF)                    │  ← Views, Behaviors, custom controls
└──────────────────────────────────────────────────────────┘
                            ▲ Bindings, ICommand
┌──────────────────────────────────────────────────────────┐
│                Foliant.ViewModels (MVVM)                 │  ← ObservableObject, RelayCommand
└──────────────────────────────────────────────────────────┘
                            ▲ Сценарии (use cases)
┌──────────────────────────────────────────────────────────┐
│              Foliant.Application (use cases)             │  ← OpenDocumentUseCase, RenderPageUseCase, ...
└──────────────────────────────────────────────────────────┘
                            ▲ Контракты Domain + контракты ports
┌──────────────────────────────────────────────────────────┐
│                Foliant.Domain (pure, no IO)              │  ← IDocument, IPageRender, RenderOptions, ...
└──────────────────────────────────────────────────────────┘
                            ▲ Реализации портов
┌──────────────────────────────────────────────────────────┐
│   Foliant.Infrastructure   │  Foliant.Engines.{Pdf,Ocr}  │
│   Cache, Settings,         │  PDFiumCore, Tesseract,     │
│   Storage, Search, ...     │  PdfPig                     │
└──────────────────────────────────────────────────────────┘
                            ▲ MEF discovery (опц. плагины)
┌──────────────────────────────────────────────────────────┐
│   plugins/Foliant.Plugin.DjVu  │  pro/Foliant.Pro.*      │
│   (out-of-process)             │  (закрытый код)         │
└──────────────────────────────────────────────────────────┘
```

## 2. Правила зависимостей (enforce'им через `dotnet list reference` в CI)

| Проект | Может ссылаться на |
|---|---|
| `Foliant.Domain` | Только `Microsoft.Extensions.Logging.Abstractions`. Никаких UI / IO / SQLite. |
| `Foliant.Application` | `Foliant.Domain` + abstractions из `Microsoft.Extensions.*`. |
| `Foliant.Infrastructure` | `Foliant.Domain` + сторонние библиотеки (SQLite, Serilog, System.Text.Json, ...). НЕ ссылается на UI, ViewModels, Engines. |
| `Foliant.Engines.*` | `Foliant.Domain` + `Foliant.Plugins.Contracts` + сторонняя нативка. |
| `Foliant.Plugins.Contracts` | `Foliant.Domain` + `System.Composition` (MEF). |
| `Foliant.ViewModels` | `Foliant.Application` + `Foliant.Domain` + `CommunityToolkit.Mvvm`. **Не** ссылается на `PresentationFramework`. |
| `Foliant.UI` | `Foliant.ViewModels`. WPF (`UseWPF=true`). |
| `Foliant.App` | Все вышеперечисленные. Composition root. |

Нарушение этих правил — **блокер review**.

## 3. Единый контракт документа

```csharp
public interface IDocument : IAsyncDisposable
{
    DocumentKind Kind { get; }
    int PageCount { get; }
    DocumentMetadata Metadata { get; }
    PageSize GetPageSize(int pageIndex);
    Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct);
    Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct);
    IDocumentEditor? GetEditor();
    IFormController? GetForms();
    ISignatureController? GetSignatures();
}
```

UI и ViewModels **не знают**, читают они PDF, DjVu или EPUB. Любой документ обрабатывается одинаково.

## 4. Маршрутизация открытия

`OpenDocumentUseCase` принимает `IEnumerable<IDocumentLoader>` (через DI) и выбирает первый, у которого `CanLoad(path) == true`. Новый формат добавляется регистрацией ещё одного `IDocumentLoader` в DI или через MEF-плагин.

```
Path → [PdfDocumentLoader.CanLoad?] → Yes → PdfDocument
                       │
                       └─→ [DjvuDocumentLoader.CanLoad?] → Yes → DjvuDocument
                                       │
                                       └─→ ... → UnsupportedDocumentException
```

## 5. Кэш

Подробности — в [`CACHE.md`](CACHE.md). Здесь короткая карта:

| Слой | Что | Где | Реализация |
|---|---|---|---|
| 1 | Page bitmap LRU | RAM | `MemoryPageCache` поверх `LruCache<CacheKey, IPageRender>` |
| 2 | Thumbnails | RAM (per-doc) | `ThumbnailCache` (S3) |
| 3 | Text/structure | RAM (per-doc) | `TextStructureCache` (S6) |
| 4 | Persistent disk | `%LOCALAPPDATA%\Foliant\Cache\pages\` + SQLite metadata | `SqliteDiskCache` |
| 5 | Full-text index | `%LOCALAPPDATA%\Foliant\Cache\index\fts.db` (FTS5) | `SqliteFtsIndex` |

`CacheJanitor : BackgroundService` тикает каждые 30 с и держит DiskCache ниже soft-limit (90 % hard).

## 6. Editor → command-pattern + event store

Любая правка документа — `IDocumentCommand` с `Apply` / `Invert`. События персистятся в append-only журнал `Autosave/{docId}/events.jsonl` мгновенно. Это даёт:

- **Undo/Redo** бесплатно.
- **Auto-save** — каждое событие на диске.
- **Crash recovery** — при старте чекаем `Autosave/*`, предлагаем восстановить.
- Базу для будущей коллаборации (CRDT/OT) — но это вне scope (см. PROJECT_BOARD раздел 12.4).

## 7. Плагины

Подробности — в [`PLUGINS.md`](PLUGINS.md). Кратко:

- **Pro-плагины** (закрытый код): MEF, in-process DLL, контракты в `Foliant.Plugins.Contracts`.
- **Out-of-process плагины** (DjVu, LibreOffice): отдельный installer, `Process.Start` per-call. GPL/MPL не «заражает» MIT-ядро.

## 8. Threading

| Слой | Поток |
|---|---|
| UI | UI-thread (WPF Dispatcher) |
| ViewModels | UI-thread; `await` для long-running через `Task.Run` или async I/O |
| Application use cases | Любой; принимают `CancellationToken` |
| Engine.Pdf (PDFium) | Per-document `SemaphoreSlim(1,1)` — PDFium не любит параллельные вызовы для одного документа |
| Cache | RAM (`MemoryPageCache`) — `Lock`; Disk (`SqliteDiskCache`) — `SemaphoreSlim` сериализует writes, reads concurrent |
| OCR | `min(4, CPU/2)` страниц параллельно |

## 9. Конфигурация

- Файл: `%APPDATA%\Foliant\settings.json`.
- Schema-versioned: `AppSettings.Version`. При unknown future schema — graceful fallback на defaults.
- Hot-reload через `FileSystemWatcher` — для удобства разработки.
- Source-generated `JsonSerializerContext` — zero-reflection.

## 10. Логирование

- Serilog → `%LOCALAPPDATA%\Foliant\Logs\foliant-{date}.log` (rolling daily, 14 файлов, 50 МБ лимит).
- Уровни: `Information` по умолчанию; `Debug` через CLI флаг (Phase 2).
- Никакого PII. Пути файлов — да, содержимое — никогда.
