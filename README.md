# Foliant

> Лёгкая полностью оффлайн Windows-альтернатива Adobe Acrobat Pro.
> Без AI, без облака, с акцентом на OCR, inline-редактирование PDF, точную конвертацию в DOCX/XLSX и полноценную поддержку DjVu.

[![CI](https://github.com/flowa7021-source/Reader/actions/workflows/ci.yml/badge.svg)](https://github.com/flowa7021-source/Reader/actions/workflows/ci.yml)
[![CodeQL](https://github.com/flowa7021-source/Reader/actions/workflows/codeql.yml/badge.svg)](https://github.com/flowa7021-source/Reader/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Статус

**Phase 1 — Альфа, в работе.** Базовая инфраструктура готова (просмотр PDF на PDFium, 5-слойный кэш, FTS5-поиск, настройки/локализация/recents); «несущие» интеграции (OCR-движок, DjVu-плагин, реальное управление страницами, редактор/DOCX, визуальный слой аннотаций, хранилище лицензий, инсталлятор) — ещё впереди. Срез готовности по спринтам S1–S13 — в [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) (раздел 4.0). Альфа (v0.1) — цель Q3 2026. Roadmap в [`PROJECT_BOARD.md`](PROJECT_BOARD.md).

Заявленные 5 форматов (PDF, DjVu, Image, EPUB, FB2, MOBI) **открываются** в Phase 1, но EPUB/FB2/MOBI пока работают в режиме «read-only через текстовый слой»: документ индексируется для поиска и FTS, навигация по разделам доступна, однако страница рисуется белым холстом. Полноценный визуальный рендер этих форматов запланирован на Phase 2 (треки D6b/D8b). PDF, DjVu и изображения рендерятся полноценно уже сейчас.

## Документы проекта

| Документ | Назначение |
|---|---|
| [`PROJECT_BOARD.md`](PROJECT_BOARD.md) | Концепт, решения, риски, фазы. Что строим и зачем. |
| [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) | Контракт качества кода, скелет solution, спринты Phase 1, контракты Domain. Как строим. |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Как контрибьютить: код-стиль, ветвление, коммиты, тесты. |
| [`CHANGELOG.md`](CHANGELOG.md) | История релизов (Keep a Changelog). |
| [`SECURITY.md`](SECURITY.md) | Политика приёма уязвимостей. |
| [`NOTICE.md`](NOTICE.md) | Третьи лицензии используемых компонентов. |

## Стек

- **C# / .NET 10 LTS** + **WPF + MVVM** (CommunityToolkit.Mvvm)
- **PDFium** (рендер) + **PdfPig** (модификация структуры) + **PaddleOCR** через Sdcb (OCR, in-process)
- **DjVu** через опциональный out-of-process плагин (DjVuLibre)
- **SQLite + FTS5** для поиска
- **Inno Setup** для инсталлятора
- **Open-core**: ядро MIT, Pro-функции — закрытый код

## Сборка

Требуется .NET 10 SDK. Подробности и кросс-платформенные нюансы — в [`docs/BUILD.md`](docs/BUILD.md).

**Linux / macOS** (cross-platform слой — Domain / Application / Infrastructure / ViewModels / Engines / Plugins, без WPF UI):

```bash
git clone https://github.com/flowa7021-source/Reader.git
cd Reader
dotnet build Foliant.CrossPlatform.slnf -c Release -f net10.0 -warnaserror
dotnet test  Foliant.CrossPlatform.slnf -c Release -f net10.0 --filter "Category!=Slow&Category!=Integration&Category!=E2E"
```

**Windows** (полная сборка с WPF UI + нативка PDFium/PaddleOCR; необходима для запуска самого приложения):

```powershell
git clone https://github.com/flowa7021-source/Reader.git
cd Reader
pwsh tools/fetch-natives.ps1
dotnet build Foliant.sln -c Release -warnaserror
dotnet test  Foliant.sln -c Release --filter "Category!=Slow&Category!=E2E"
```

## Лицензия

[MIT](LICENSE) для ядра. Pro-функции и опциональные плагины (DjVu, LibreOffice) — отдельно, см. [`NOTICE.md`](NOTICE.md).
