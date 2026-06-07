# NOTICE — Третьи лицензии

Foliant использует следующие сторонние компоненты. Полные тексты лицензий устанавливаются вместе с приложением в `%ProgramFiles%\Foliant\Licenses\`.

## Основная поставка (включено в инсталлятор)

| Компонент | Версия (на момент записи) | Лицензия | Назначение |
|---|---|---|---|
| .NET 10 LTS Runtime | 10.x | MIT | Среда выполнения |
| PDFium (через PDFiumCore) | upstream | BSD-3-Clause / Apache-2.0 | Рендеринг PDF |
| PdfPig | 0.1.x | Apache-2.0 | Модификация структуры PDF |
| PaddleOCR (через Sdcb.PaddleOCR) | 2.x | Apache-2.0 (upstream PaddleOCR), MIT (Sdcb-обёртки) | OCR-движок |
| PaddleInference runtime (Sdcb.PaddleInference.runtime.win64.mkl) | 2.x | Apache-2.0 | Нативный inference-рантайм OCR (win-x64) |
| OpenCvSharp4 (+ runtime.win) | 4.x | Apache-2.0 (OpenCvSharp), BSD-3 (OpenCV native) | Обработка изображений для OCR |
| SixLabors.ImageSharp | 3.x | Apache-2.0 (Six Labors Split) | Препроцессинг изображений; растровый буфер HTML-рендера |
| SixLabors.ImageSharp.Drawing | 2.x | Apache-2.0 (Six Labors Split) | 2D-растеризация HTML-рендера (текст, картинки) |
| SixLabors.Fonts | 2.x | Apache-2.0 (Six Labors Split) | Загрузка/измерение/шейпинг шрифтов HTML-рендера |
| AngleSharp (+ AngleSharp.Css) | 1.x | MIT | HTML5 + CSS парсинг для рендера EPUB/FB2/MOBI |
| Liberation Fonts (встроены в `Foliant.Rendering.Html`) | 2.x | SIL OFL 1.1 | Встроенные шрифты HTML-рендера (Serif/Sans/Mono, детерминизм/CI) |
| Microsoft.Data.Sqlite | 9.x | MIT | SQLite + FTS5 |
| BouncyCastle.Cryptography | 2.x | MIT-style | Криптография, подписи |
| CommunityToolkit.Mvvm | 8.x | MIT | MVVM helpers |
| Serilog | 4.x | Apache-2.0 | Логирование |
| Microsoft.Extensions.* | 9.x | MIT | DI, Hosting, Configuration, Localization |
| System.Composition | 9.x | MIT | MEF |
| Inno Setup | 6.x | Modified BSD | Инсталлятор (build-time) |

## Опциональные плагины (скачиваются отдельно)

| Компонент | Лицензия | Изоляция |
|---|---|---|
| DjVuLibre (`ddjvu`, `djvused`) | GPL-2.0 | Отдельный плагин-инсталлятор. Out-of-process (per-call). GPL не «заражает» ядро. |
| LibreOffice headless | MPL-2.0 + LGPL | Отдельный плагин-инсталлятор. Out-of-process. |

## Анализаторы и инструменты разработки (build-time)

| Компонент | Лицензия |
|---|---|
| Roslynator.Analyzers | Apache-2.0 |
| BenchmarkDotNet | MIT |
| xunit | Apache-2.0 |
| FluentAssertions | Apache-2.0 |
| NSubstitute | BSD-3-Clause |
| FsCheck | BSD-3-Clause |
| Verify | MIT |
| coverlet | MIT |

## Эталонные тестовые ассеты (`tests/assets/`)

См. [`tests/assets/README.md`](tests/assets/README.md) — для каждого файла указан источник и лицензия (CC0 / public domain / собственный).

## Обновление этого документа

При добавлении / удалении / обновлении мажорной версии любого NuGet-пакета или нативной зависимости — обязательно обновить таблицу в этом же PR. CI отдельной проверкой это не ловит.
