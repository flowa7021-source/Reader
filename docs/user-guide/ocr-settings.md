# Настройки OCR

Foliant распознаёт текст на сканах через **PaddleOCR** (in-process, без облака).
Параметры распознавания задаются в окне настроек (**Tools → Settings**), а запуск/отмена —
через меню **Tools → Run OCR / Cancel OCR**. Источник полей — `SettingsViewModel`
(`src/Foliant.ViewModels/SettingsViewModel.cs`).

## Параметры

| Параметр | Поле VM | По умолчанию | Что делает |
|---|---|---|---|
| Язык(и) распознавания | `OcrLanguage` | `rus+eng` | Набор языков в стиле Tesseract (`eng`, `rus`, `eng+rus`, …). Определяет, какая модель-распознаватель используется. |
| Макс. параллельных страниц | `MaxParallelOcrPages` | `4` | Сколько страниц распознаётся одновременно при OCR всего документа. |
| Авто-OCR открытых сканов | `AutoOcrOpenedScans` | выкл. | Если включено — запускать OCR автоматически при открытии скан-документа. |

Значения по умолчанию заданы в `OcrSettings`
(`src/Foliant.Application/Settings/AppSettings.cs`). Кнопка **Reset to defaults** в окне
настроек возвращает все поля к `AppSettings.Default`.

## Языки и выбор модели

Строка языков парсится по `+` и преобразуется в один из двух наборов моделей-распознавателей
PaddleOCR (`OcrLanguageMap`, `src/Foliant.Engines.Ocr/OcrLanguageMap.cs`):

- **cyrillic** — если среди запрошенных языков есть хотя бы один кириллический
  (`rus`, `ukr`, `bel`, `kaz`, `bul`, `srp`, `mkd`, `mon`, `tgk`, `kir`). Эта модель
  распознаёт и латиницу тоже, поэтому приоритетная связка `rus+eng` покрывается одной моделью.
- **latin** — иначе (`eng`, `deu`, `fra`, `spa`, `ita`, …).

Детектор текста (`det`) и классификатор поворота (`cls`) — общие для всех языков; по скрипту
выбирается только распознаватель (`rec`).

!!! note "Один проход — одна модель"
    Правило сознательно простое: смешанный кириллица+латиница документ распознаётся
    моделью `cyrillic`. Отдельные модели для CJK/арабского подключаются только в tier **Full**
    (см. ниже).

## Tier'ы и загрузка моделей

Модели PaddleOCR не входят в репозиторий — они скачиваются оффлайн скриптом
`tools/fetch-natives.ps1` и раскладываются в `native/paddleocr/` рядом с приложением.
Скрипт принимает параметр `-Tier` (по умолчанию `Basic`):

| Tier | Наборы распознавателей | Назначение |
|---|---|---|
| `Basic` | `latin`, `cyrillic` | Европа + рус/СНГ (по умолчанию). |
| `Standard` | `latin`, `cyrillic` | То же, что Basic. |
| `Full` | `latin`, `cyrillic`, `chinese`, `japan`, `korean`, `arabic` | Плюс CJK и арабский. |

Детектор и классификатор поворота скачиваются всегда, независимо от tier'а.

```powershell
# Базовый набор (латиница + кириллица)
pwsh tools/fetch-natives.ps1

# Полный набор (включая CJK и арабский)
pwsh tools/fetch-natives.ps1 -Tier Full
```

Раскладка на диске:

```text
native/paddleocr/det/             детектор текста (общий)
native/paddleocr/cls/             классификатор поворота (общий)
native/paddleocr/rec/latin/       распознаватель латиницы (+ label.txt)
native/paddleocr/rec/cyrillic/    распознаватель кириллицы (+ label.txt)
native/paddleocr/rec/<script>/    прочие скрипты для tier Full
```

Каждый файл проверяется по SHA256 из `tools/third-party/checksums.json`; если файл уже
скачан и хэш совпадает — повторная загрузка не выполняется. Если моделей нет, движок
бросает понятную ошибку с подсказкой запустить `fetch-natives.ps1`
(`src/Foliant.Engines.Ocr/PaddleOcrEngine.cs`).
