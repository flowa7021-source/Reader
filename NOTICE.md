# NOTICE — Третьи лицензии

Foliant использует следующие сторонние компоненты. Полные тексты лицензий устанавливаются вместе с приложением в `%ProgramFiles%\Foliant\Licenses\`.

## Основная поставка (включено в инсталлятор)

| Компонент | Версия (на момент записи) | Лицензия | Назначение |
|---|---|---|---|
| .NET 10 LTS Runtime | 10.x | MIT | Среда выполнения |
| PDFium (через PDFiumCore) | upstream | BSD-3-Clause / Apache-2.0 | Рендеринг PDF |
| PdfPig | 0.1.x | Apache-2.0 | Модификация структуры PDF |
| Tesseract (через Tesseract NuGet) | 5.x | Apache-2.0 | OCR-движок |
| SixLabors.ImageSharp | 3.x | Apache-2.0 (Six Labors Split) | Препроцессинг изображений |
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
