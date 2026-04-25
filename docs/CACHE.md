# Cache

Подробное описание кэш-подсистемы. Дополняет раздел 5.1 `IMPLEMENTATION_PLAN.md`. Реализация в `src/Foliant.Infrastructure/Caching/`.

## 1. Зачем кэшировать

| Что повторяется | Стоимость пересчёта | Решение |
|---|---|---|
| Рендер страницы (zoom, scroll) | 50–500 мс | Слой 1 (RAM bitmap LRU) |
| Текстовый слой | 10–100 мс/стр | Слой 3 (RAM text/structure) |
| Превью-thumbnails | 30–200 мс/стр | Слой 2 (RAM, per-doc) |
| OCR страницы скана | 1–10 с | Слой 4 (persistent, главная экономия) |
| Конвертация PDF→DOCX | 5–60 с | Слой 4 |
| Полнотекстовый индекс | минуты | Слой 5 (persistent FTS5) |

## 2. Слои

### Слой 1 — `MemoryPageCache`
- `LruCache<CacheKey, IPageRender>` с capacity-by-bytes (`Stride×HeightPx`).
- Лимит RAM: `min(15 % памяти системы, 1 ГБ, ≥ 128 МБ)`.
- Sticky-окно ±5 страниц от `SetCurrent(docFp, pageIndex)` — re-touch при каждом `Put`, чтобы LRU не выгнал.
- Авто-Dispose `IPageRender` при эвикции (нативные битмапы).

### Слой 2 — `ThumbnailCache` (S3, ещё не реализован)
- `Dictionary<int, BitmapSource>` per document, всегда в памяти, пока документ открыт.
- Размер превью: 96×128 px.

### Слой 3 — `TextStructureCache` (S6, ещё не реализован)
- `Dictionary<int, TextLayer>` per document.
- `Lazy<DocumentOutline>` per document (bookmarks, links).

### Слой 4 — `SqliteDiskCache`
- Файлы в `%LOCALAPPDATA%\Foliant\Cache\pages\{key.ToFileName()}`.
- Метаданные в `metadata.db` (SQLite, WAL, synchronous=NORMAL):

  ```sql
  CREATE TABLE entries (
    key         TEXT PRIMARY KEY,
    size        INTEGER NOT NULL,
    last_access INTEGER NOT NULL,
    doc_fp      TEXT    NOT NULL
  );
  CREATE INDEX ix_entries_last_access ON entries(last_access);
  CREATE INDEX ix_entries_doc_fp      ON entries(doc_fp);
  ```

- Атомарная запись: `.tmp` → `Move(overwrite)`.
- Concurrent: `SemaphoreSlim` сериализует writes; reads (`TryGet`) — concurrent.
- Лимит диска: 5 ГБ default, настраивается 500 МБ – 50 ГБ.

### Слой 5 — `SqliteFtsIndex`
- `%LOCALAPPDATA%\Foliant\Cache\index\fts.db`.
- Таблицы:

  ```sql
  CREATE TABLE documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    fp TEXT NOT NULL UNIQUE,
    path TEXT NOT NULL,
    page_count INTEGER NOT NULL DEFAULT 0,
    last_indexed INTEGER NOT NULL
  );
  CREATE VIRTUAL TABLE pages_fts USING fts5(
    doc_id UNINDEXED,
    page_index UNINDEXED,
    text,
    tokenize = 'unicode61 remove_diacritics 2'
  );
  ```

- Поиск: `MATCH` + `bm25` ранжирование + `snippet(...)` для подсветки.
- Реиндексация документа удаляет старые страницы (всё через одну транзакцию).

## 3. Ключ кэша

```csharp
public sealed record CacheKey(
    string DocFingerprint,   // sha256(first 64 KB ‖ size ‖ mtime)
    int    PageIndex,
    int    EngineVersion,    // semver пакета engine
    int    ZoomBucket,       // round(zoom * 100 / 25) * 25
    int    Flags);           // bit0=annotations, bits1+=Theme
```

`ToFileName()` стабилен между процессами и версиями приложения. Меняется только при смене `EngineVersion` или `Flags`-конвенции.

## 4. Fingerprint файла

`FileFingerprint`:
- `sha256(first 64 KB || size_le || mtime_ticks)` → hex (64 символа).
- ArrayPool для буфера, async I/O.
- Раз в N открытий — фоновое вычисление полного хэша всего файла (Phase 2).

## 5. Инвалидация

Триггеры:

1. **Fingerprint файла изменился** → выкидываем все записи документа (`InvalidateDocumentAsync(fp)` — одна транзакция).
2. **Версия рендер-движка обновилась** → постепенный LRU-eviction. EngineVersion в ключе → старые ключи перестанут попадаться.
3. **Версия OCR-движка/моделей изменилась** → переиндексация в фоне.
4. **Ручная очистка** — Settings → Очистить кэш.
5. **Превышение лимита** → `CacheJanitor` тикает раз в 30 с, эвиктит до soft-limit (90 % hard).

## 6. Фоновая поддержка — `CacheJanitor`

`BackgroundService`:
- `PeriodicTimer` тиком 30 с.
- `if currentSize > hardLimit → EvictToTargetAsync(soft)`.
- Soft = 90 % hard по умолчанию.
- Memory pressure (через `QueryMemoryResourceNotification`) — Phase 2: внеплановая очистка 50 % RAM-кэшей.

## 7. Безопасность

- NTFS ACL: cache-каталог доступен **только Current User**. Создаётся через `Directory.CreateDirectory` от лица пользователя — наследует ACL родителя `%LOCALAPPDATA%`.
- **Защищённые паролем PDF** не кэшируются по умолчанию. Опция: DPAPI-шифрованный кэш.
- Удаление из Recents → диалог «удалить и кэш этого документа?».

## 8. Метрики (для performance baseline)

| Сценарий | Источник | Цель |
|---|---|---|
| Open 500-стр PDF (cold) | App + Slot 1 + 4 | p50 ≤ 250 мс, p95 ≤ 500 мс |
| Render page 1080p (cold) | Engine + Slot 1 | p50 ≤ 80 мс, p95 ≤ 150 мс |
| Render page 1080p (warm) | Slot 1 hit | p50 ≤ 3 мс, p95 ≤ 8 мс |
| Search across 10k pages | Slot 5 | p50 ≤ 600 мс, p95 ≤ 1500 мс |

См. `tests/Foliant.Performance/baseline.json`.

## 9. UI — что видит пользователь

- **Settings → Cache**: размер, кнопка «Очистить», слайдер лимита (500 МБ – 50 ГБ), чекбокс «Не кэшировать этот документ», чекбокс «Очищать кэш при выходе».
- **Recents → правый клик**: «Удалить» / «Удалить и кэш».
- **Status bar** (Phase 2): индикатор «Cache: 3.2/5.0 ГБ».
