# third-party

## Назначение

Хранилище пин-листа SHA256 для нативных зависимостей, которые не могут (или нежелательно) распространяться через NuGet:

- `tessdata` LSTM модели языков (большие, лежат в `tesseract-ocr/tessdata_fast` GitHub Releases).
- `DjVuLibre` бинарники (плагин, S9).
- `LibreOffice portable` (плагин, Phase 3).

## Файл `checksums.json`

```json
{
  "tessdata": {
    "rus": {
      "url":    "https://github.com/tesseract-ocr/tessdata_fast/raw/main/rus.traineddata",
      "sha256": "<заполнить при первом фиксе версии>"
    },
    "eng": { "url": "...", "sha256": "..." }
  },
  "djvulibre": { "url": "...", "sha256": "..." }
}
```

Файл создаётся в S8 (OCR pipeline), когда впервые выбираем версии. До этого `fetch-natives.ps1` корректно выходит без ошибки.

## Правила обновления

1. Любое обновление URL → обязательно пересчитать SHA256.
2. PR с обновлением `checksums.json` обязан включать обновление соответствующей строки в `NOTICE.md`.
3. Деградация безопасности (downgrade версии) — только с обоснованием в PR-описании и approve мейнтейнера.
