# Changelog

Все заметные изменения проекта документируются здесь.

Формат: [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/).
Версии: [Semantic Versioning 2.0.0](https://semver.org/lang/ru/).

## [Unreleased]

### Added
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

[Unreleased]: https://github.com/flowa7021-source/Reader/compare/HEAD
