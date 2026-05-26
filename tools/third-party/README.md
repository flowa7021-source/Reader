# third-party

## Назначение

Хранилище пин-листа SHA256 для нативных зависимостей, которые не могут (или нежелательно) распространяться через NuGet:

- Модели PaddleOCR (детектор `det`, классификатор `cls`, распознаватели `rec_<script>` по скриптам) — раскладываются в `native/paddleocr/`.
- `DjVuLibre` бинарники (плагин, S9).
- `LibreOffice portable` (плагин, Phase 3).

## Файл `checksums.json`

```json
{
  "paddleocr": {
    "det":          { "url": "...", "sha256": "<заполнить при первом фиксе версии>" },
    "cls":          { "url": "...", "sha256": "..." },
    "rec_latin":    { "url": "...", "sha256": "..." },
    "rec_cyrillic": { "url": "...", "sha256": "..." }
  },
  "djvulibre": { "url": "...", "sha256": "..." }
}
```

Файл создаётся в S8 (OCR pipeline), когда впервые выбираем версии. До этого `fetch-natives.ps1` корректно выходит без ошибки.

## Раскладка моделей (контракт с движком)

`fetch-natives.ps1` скачивает каждый `model.tar` и распаковывает его (`tar -xf`) в целевой каталог.
`PaddleOcrEngine` (`src/Foliant.Engines.Ocr/PaddleOcrEngine.cs:114-129`) грузит модели так:

| Каталог                          | Что нужно внутри (на верхнем уровне)                                  |
|----------------------------------|-----------------------------------------------------------------------|
| `native/paddleocr/det/`          | `inference.pdmodel` + `inference.pdiparams` (`DetectionModel.FromDirectory`, V4) |
| `native/paddleocr/cls/`          | `inference.pdmodel` + `inference.pdiparams` (`ClassificationModel.FromDirectory`) |
| `native/paddleocr/rec/<script>/` | infer-файлы **+ `label.txt`** (словарь) (`RecognizationModel.FromDirectory`, V4) |

ВАЖНО при заполнении `checksums.json`:

1. **Плоская раскладка.** Файлы модели должны лежать прямо в каталоге, а не во вложенной папке.
   Официальные PaddleOCR-`.tar` распаковываются в подкаталог `*_infer/` — такой архив нужно
   **перепаковать плоско**, иначе `FromDirectory` не найдёт модель.
2. **`label.txt` для rec.** Движок передаёт `recDir/label.txt` явно; апстрим-`.tar` распознавателя
   словарь обычно не содержит — вложите нужный dict в архив как `label.txt`.
3. Имена ключей: `det`, `cls`, `rec_latin`, `rec_cyrillic` (+ `rec_chinese|japan|korean|arabic` для Full).
   Скрипт кладёт их в `det/`, `cls/`, `rec/<script>/` соответственно.

## Правила обновления

1. Любое обновление URL → обязательно пересчитать SHA256.
2. PR с обновлением `checksums.json` обязан включать обновление соответствующей строки в `NOTICE.md`.
3. Деградация безопасности (downgrade версии) — только с обоснованием в PR-описании и approve мейнтейнера.
