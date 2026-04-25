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

[Unreleased]: https://github.com/flowa7021-source/Reader/compare/HEAD
