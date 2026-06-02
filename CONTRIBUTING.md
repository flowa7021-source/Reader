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

Pre-push hook (Husky.Net) автоматически: `dotnet format whitespace`/`style --verify-no-changes`
+ `dotnet build -warnaserror` + быстрые unit-тесты (`Category!=Slow&Category!=Integration&Category!=E2E`)
по `Foliant.CrossPlatform.slnf`. Ставится при первом `dotnet restore` (см. ниже «Установка»).
Bypass (для экстренных случаев): `HUSKY=0`, `SKIP_HOOKS=1` или `git push --no-verify`.

## Установка pre-push hook (Husky.Net)

Ничего отдельно делать не надо. Первый же `dotnet build` (или `dotnet test`) после
`git clone` триггерит MSBuild-таргет `HuskyInstall` в `Directory.Build.targets`, который
выполняет `dotnet tool restore` + `dotnet husky install` (инкрементно — повторно не
запускается, пока `.config/dotnet-tools.json` не изменится). В результате прописывается
`git config core.hooksPath .husky` и pre-push hook начинает работать.

> **Заметка.** Голый `dotnet restore` НЕ триггерит `AfterTargets="Restore"` — это известное
> ограничение .NET SDK ([dotnet/sdk#7741](https://github.com/dotnet/sdk/issues/7741)). Поэтому
> auto-install крепится к first `dotnet build`/`dotnet test`, который и так делает implicit
> restore. На практике у любого contributor'а первый шаг после клона — это `dotnet build`/`test`,
> так что hook ставится автоматически до первого `git push`.

Если auto-install не сработал (например, build запускался с `HUSKY=0`), запустить вручную
из корня репо:

```bash
dotnet tool restore
dotnet husky install
```

Bypass для экстренных push'ей:
- `HUSKY=0 git push` — глобальный off-switch Husky.Net.
- `SKIP_HOOKS=1 git push` — локальный off-switch для этого hook'а.
- `git push --no-verify` — git встроенный обход всех hook'ов.

## PR

- ≤ 400 LOC изменений. Исключения: добавление test assets, авто-генерация.
- Один PR — один логический change.
- Squash merge как дефолт.
- Шаблон PR ([`pull_request_template.md`](.github/pull_request_template.md)) — обязателен полностью.

## Запуск тестов

**Одной командой перед коммитом (рекомендуется):**

```bash
tools/verify-local.sh
```

Собирает кросс-платформенный слой (`Foliant.CrossPlatform.slnf`) под `-warnaserror` и гоняет
быстрые unit- + интеграционные (real-SQLite / ddjvu) тесты — тот же набор, что Linux-джоб
`verify.yml`. WPF и нативка проверяются только Windows-джобом CI.

Связанные блокирующие гейты (тоже в `verify.yml`):

- `tools/check-coverage` — пороги покрытия §6.2 (Domain 90 / App 80 / Infra 70 / VM 60).
- `tools/perf-compare` — сравнение BenchmarkDotNet с `baseline.json`; регрессия > 15 % p95 — блокер.

Точечно:

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
