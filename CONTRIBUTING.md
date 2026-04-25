# Контрибьютинг

Спасибо за интерес к Foliant. Этот документ — обязательная памятка перед первым PR.

## Контракт качества кода

См. раздел 0 [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md). Любой PR проходит через эти фильтры. Нарушения блокируют ревью.

Кратко:
- Простейшее решение, удовлетворяющее acceptance. Не делаем «на будущее».
- Файл ≤ 300 строк. Метод ≤ 30 строк. Класс ≤ 7 публичных членов.
- NRT включён, `TreatWarningsAsErrors=true`. Хочешь warning — `#pragma` с TODO+датой.
- File-scoped namespaces, primary constructors, pattern matching, records для DTO.
- Один `public class` на файл; имя файла = имя типа.
- Без regions. Без мульти-абзацных XML-doc. Комментарии — только WHY.
- `async`/`await` везде где IO. Никаких `.Result` / `.Wait()`.
- `CancellationToken` обязателен в каждой `async`-операции > 50 мс.

## Дерево репо

См. раздел 1 [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md).

## Ветвление

| Тип | Префикс | Пример |
|---|---|---|
| Фича | `feat/<sprint>-<short>` | `feat/s3-disk-cache` |
| Багфикс | `fix/<short>` | `fix/null-on-empty-pdf` |
| Рефакторинг без поведения | `refactor/<short>` | `refactor/extract-cache-key` |
| Документация | `docs/<short>` | `docs/update-build-instructions` |
| Прочее | `chore/<short>` | `chore/bump-deps` |

`main` защищён: PR обязателен, нужен зелёный CI и одно ревью.

## Коммиты — Conventional Commits

```
<type>(<scope>): <subject>

<body — что и зачем, не как>

<footer — refs, breaking changes>
```

Типы: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `chore`, `build`, `ci`.

Pre-commit hook (Husky.Net, опц.) автоматически: `dotnet format` + unit-тесты на затронутых проектах.

## PR

- ≤ 400 LOC изменений. Исключения: добавление test assets, авто-генерация.
- Один PR — один логический change.
- Squash merge как дефолт.
- Шаблон PR ([`pull_request_template.md`](.github/pull_request_template.md)) — обязателен полностью.

## Запуск тестов

```powershell
# Быстрые (PR-набор)
dotnet test --filter "Category!=Slow&Category!=E2E"

# Все
dotnet test

# Покрытие
dotnet test --collect:"XPlat Code Coverage"
```

Цели покрытия unit-тестами:
- `Foliant.Domain` ≥ 90 %
- `Foliant.Application`, `Foliant.Infrastructure` ≥ 80 % / 70 %
- `Foliant.ViewModels` ≥ 60 %
- Engines/Plugins — integration, не unit
- UI Views — не измеряем

## Performance

Изменение горячих путей (рендер, кэш, OCR, поиск) → запусти `Foliant.Performance` локально, приложи дельту в PR-описание. Регрессия > 15 % p95 — блокер.

## Перед PR — чек-лист

- [ ] `dotnet format --verify-no-changes` — green
- [ ] `dotnet build -warnaserror` — green
- [ ] `dotnet test` — green
- [ ] `CHANGELOG.md` — добавлена запись в `[Unreleased]` (для feat/fix/perf)
- [ ] `docs/` обновлён, если поведение видимо пользователю
- [ ] Скриншот в PR — для UI-изменений

## Где обсуждать дизайн

- Архитектурное решение → Issue с лейблом `design-discussion`, формат: проблема → варианты → выбор → следствия. Закрепляется в `PROJECT_BOARD.md` или `IMPLEMENTATION_PLAN.md`.
- Багрепорт → Issue с лейблом `bug` + минимальный repro.
- Вопрос → Discussions (категория Q&A).
