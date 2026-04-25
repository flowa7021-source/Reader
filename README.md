# Foliant

> Лёгкая полностью оффлайн Windows-альтернатива Adobe Acrobat Pro.
> Без AI, без облака, с акцентом на OCR, inline-редактирование PDF, точную конвертацию в DOCX/XLSX и полноценную поддержку DjVu.

[![CI](https://github.com/flowa7021-source/Reader/actions/workflows/ci.yml/badge.svg)](https://github.com/flowa7021-source/Reader/actions/workflows/ci.yml)
[![CodeQL](https://github.com/flowa7021-source/Reader/actions/workflows/codeql.yml/badge.svg)](https://github.com/flowa7021-source/Reader/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Статус

**Phase 0 — Подготовка.** Альфа (v0.1) — цель Q3 2026. Подробный roadmap в [`PROJECT_BOARD.md`](PROJECT_BOARD.md), детальный план реализации в [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md).

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
- **PDFium** (рендер) + **PdfPig** (модификация структуры) + **Tesseract LSTM** (OCR)
- **DjVu** через опциональный out-of-process плагин (DjVuLibre)
- **SQLite + FTS5** для поиска
- **Inno Setup** для инсталлятора
- **Open-core**: ядро MIT, Pro-функции — закрытый код

## Сборка

```powershell
# Phase 0: build instructions появятся после S3 (см. IMPLEMENTATION_PLAN.md, Phase 0/неделя 3).
# Кратко (когда будет готово):
git clone https://github.com/flowa7021-source/Reader.git
cd Reader
pwsh tools/fetch-natives.ps1
dotnet build -c Release
dotnet test
```

## Лицензия

[MIT](LICENSE) для ядра. Pro-функции и опциональные плагины (DjVu, LibreOffice) — отдельно, см. [`NOTICE.md`](NOTICE.md).
