# Ретроспектива: CI-раунды, закономерности ошибок и слабые места

> Документ для учёта системных слабых мест в дальнейшей разработке. Составлен после
> приведения ветки `claude/modest-shannon-Z9hWF` к зелёной сборке и тестам через 5
> раундов исправлений (PR #19). Не история коммитов — а **каталог провалов с правилами**,
> чтобы не повторять их.

## 0. Мета-первопричина (одна на всё)

**Весь код писался без среды сборки** (нет .NET SDK; WPF/нативка требуют Windows). Поэтому
ничто не проверялось против компилятора, графа NuGet, аналайзеров и рантайма до самого CI.
Накопился «долг первой компиляции», который вскрылся волнами (5 раундов: restore → Domain →
Application/plugins → Infra/Engines/VM → хвосты → рантайм-тесты).

**Вывод №1:** отсутствие цикла «написал → собрал → прогнал» — корень ВСЕХ классов ниже.
Любая будущая фича должна иметь способ компиляции/тестов до пуша (Windows-CI как `verify.yml`
или локально), иначе долг повторится.

## 1. Каталог классов провалов (с правилами на будущее)

| Класс | Конкретные случаи | Корневая причина | Правило на будущее |
|---|---|---|---|
| **Угаданные версии пакетов** | NU1603 `Sdcb.PaddleInference 2.6.0` не существует; NU1903 `System.IO.Packaging 8.0.0` CVE через OpenXml 3.1.0; NU1510 `Microsoft.Win32.Registry` лишний на net-windows | Версии/пакеты выдуманы по памяти | **Сверять граф зависимостей по nuspec ДО объявления** (`api.nuget.org/.../{id}.nuspec`). Версию транзитивов с CVE — поднимать через прямой пакет/апгрейд родителя. На `net*-windows` не тащить framework-provided пакеты |
| **Угаданные внешние API** | `DetectionModel.FromDirectory` без `ModelVersion`; неуверенность по PaddleOcrAll/PdfPig writer/PDFium FPDFText | Сигнатуры по памяти | **Перед использованием сверять API по исходнику/доке нужной версии** (WebFetch nuspec/README/source). Компилятор — финальный арбитр, но не первый |
| **Nullable / валидация** | CA1062 (`License.Expired`, `TrialAntiTamperService.Evaluate`); CA2264 (`ThrowIfNull(page)` на `record struct`); CS8602 (`Document!.Body`) | Путаница value-vs-reference + ThrowIfNull; flow-анализ не видит null-guard через bool-флаги | **value-типы не проверять на null**; в public-методах валидировать разыменовываемые reference-аргументы; null-guard писать прямыми `is null`-проверками (не через промежуточный bool), чтобы flow-анализ доказал non-null |
| **Глобализация** | CA1305 (`AppendLine($"Page {i}")`) | Форматирование чисел без `IFormatProvider` | **Числовые интерполяции/format → с `CultureInfo.InvariantCulture`** (для не-UI/диагностики) |
| **Обработка исключений** | CA1031 ×3 (пустые general-catch в cleanup/fire-and-forget); CA2201 (`throw new Exception`) | `catch {}` / `throw Exception` по привычке | **Глотающий catch → конкретные типы** (`IOException`/`UnauthorizedAccessException`); намеренный широкий catch → `[SuppressMessage]` с обоснованием; rethrow (`throw;`) допустим. Бросать конкретные типы |
| **IDisposable / async** | CA2213 (`_ocrCts` не диспозился); CA2024 (`reader.EndOfStream` в async ×2); CA2025 (task/dispose в `DjvuProcessRunner`) | Незнание .NET-10 правил + забытый dispose | **Каждое IDisposable-поле диспозить**; в async не использовать `EndOfStream` (→ `while ((line = await ReadLineAsync) is not null)`); задачи на disposable дожидаться до dispose |
| **XML-doc как ошибка** | RCS1139 (нет `<summary>`); CS1574 (`cref="PropertyChanged"` не резолвится) | `GenerateDocumentationFile=true` + warnaserror превращает doc-проблемы в ошибки | **doc-комментарий → всегда `<summary>`**; `cref` только на резолвимые/квалифицированные члены, иначе `<c>`-текст |
| **Синтаксис нового C#** | CS0103 (`DefaultMaxWidthPx` в default primary-ctor-параметра не виден) | Edge-cases primary-конструкторов | **default-значение параметра primary-ctor — только литерал/внешний const**, не собственный член класса |
| **Потерянные usings при рефакторе** | CS1061 `LogError` после split partial-класса | Split на partial без компиляции — потерян `using Microsoft.Extensions.Logging` | **После любого split/перемещения — проверять usings каждого нового файла** (компиляция или ручной аудит импортов на используемые типы/extension-методы) |
| **Дрейф интерфейс/имплементация** | CS0535 `BookmarkService` не реализует `ContainsPageAsync` | Член добавлен в интерфейс без импла | **Добавил член в интерфейс → сразу реализовать во ВСЕХ impl** (cross-file consistency check) |
| **Тесты без прогона** | CS1503 (`null` в struct-параметр); sticky-тест ставил `SetCurrent` после выгона; substitute `Metadata`=null; тест ждал enum-строку | Тесты писались не запускаясь | **Тест — это код, его тоже надо прогонять**: не тестировать невозможное (null для value-типа), корректно настраивать моки, проверять предусловия сценария |
| **Логика, что компилируется, но неверна** | `MemoryPageCache.TouchSticky` угадывал ключи (`ZoomBucket=100`) + неверный порядок → центр выпадал; работало лишь на zoom 1.0 | Зелёная сборка ≠ корректность | **Вывод №2: аналайзеры/компилятор не ловят логические баги.** Нужны рантайм/integration-тесты на реальные сценарии; для «угадайных» подходов (как ключи кэша) — оперировать реальными данными, не догадками |

## 2. Закономерности (что повторялось)

1. **Делегирование агентам НЕ убирает долг** — код P-агентов (DjVu CA2025, ThumbnailRenderer CS0103, PaddleOcrEngine FromDirectory) имел те же дефекты, т.к. они тоже не компилировали. Бриф агенту должен включать те же правила §1.
2. **Ошибки шли по слоям сборки** (Domain → Application → Infra/Engines → VM → тесты) — каждый раунд вскрывал следующий проект. Это предсказуемо: при невозможности собрать локально считать, что ошибки есть в КАЖДОМ проекте, не только в первом упавшем.
3. **Строгий гейт усиливает всё**: `AnalysisMode=All` + `TreatWarningsAsErrors` + Roslynator + `GenerateDocumentationFile` превращают предупреждения и doc-мелочи в блокеры. Под таким гейтом «почти правильно» = красный CI.
4. **Сходимость по объёму ошибок**: 14 → 7 → 10 → 3 → 3(тесты). Не монотонно (раунд 3 вырос, т.к. впервые компилировались движки с угаданными API), но в целом убывает.

## 3. Пред-пуш чеклист (само-применять)

> **Автоматизировано.** Пункты «формат / build -warnaserror / быстрые тесты» исполняются
> **Husky.Net pre-push hook'ом** (`.husky/pre-push` → группа `pre-push` в `.husky/task-runner.json`,
> [Husky.Net docs](https://alirezanet.github.io/Husky.Net/)). Hook ставится автоматически при
> первом `dotnet build`/`dotnet test` (MSBuild-таргет в `Directory.Build.targets`, см.
> `CONTRIBUTING.md` → «Установка pre-push hook»). Push блокируется, если любой из этапов
> падает. Bypass (только для экстренных случаев): `HUSKY=0`, `SKIP_HOOKS=1`, либо
> `git push --no-verify`. Остальные пункты (версии NuGet, API, nullable, doc-комментарии,
> using'и, interface drift, корректность тестов, рантайм-проверки) — по-прежнему ручные:
> их не ловит ни компилятор, ни форматтер.

- [ ] Версии новых NuGet — сверены по nuspec (существуют + транзитивы без CVE + не framework-provided на net-windows).
- [ ] Внешние API (сигнатуры) — сверены по доке/исходнику нужной версии.
- [ ] Nullable: value-типы не `ThrowIfNull`; reference-аргументы public-методов валидированы; null-guard прямыми проверками.
- [ ] Форматирование чисел/дат — с `IFormatProvider`.
- [ ] `catch` — конкретные типы или обоснованный `[SuppressMessage]`; не `throw new Exception`.
- [ ] IDisposable-поля диспозятся; нет `EndOfStream`/sync-IO в async.
- [ ] Doc-комментарии: есть `<summary>`, `cref` резолвится.
- [ ] Новый файл/partial — все нужные `using` на месте.
- [ ] Добавлен член интерфейса → реализован во всех impl. *CI-гейт:* CS0535 ловится явно на обеих платформах — Linux-джоб `verify.yml` собирает `Foliant.CrossPlatform.slnf -warnaserror` (Domain/Application/Infrastructure/ViewModels/Engines/Plugins), Windows-джобы `verify.yml` и `ci.yml` собирают полный `Foliant.sln -warnaserror` (включая Foliant.App/Foliant.UI и Windows-only TFM), плюс safety-net grep по build-логу в `verify.yml`.
- [ ] Тесты: не проверяют невозможное; моки настроены; предусловия сценария корректны.
- [ ] Есть рантайм/integration-проверка для логики, которую аналайзер не поймает.

## 4. Два главных вывода

1. **Цикл сборки обязателен.** Без локальной/CI компиляции долг неизбежен.
   **РЕШЕНО:** не-WPF проекты переведены на `net10.0` (Infrastructure/DjVu — мульти-таргет
   с `#if WINDOWS` для DPAPI/реестра), поэтому весь логический слой (Domain, Application,
   Infrastructure, ViewModels, Engines, плагин) собирается и юнит-тестируется на Linux:
   `tools/install-dotnet.sh` ставит SDK, `tools/verify-local.sh` гоняет
   `Foliant.CrossPlatform.slnf` под `-warnaserror` + быстрые тесты (~700 шт, секунды) ДО коммита.
   `verify.yml` теперь: быстрый Linux-джоб (этот же слой) + полный Windows build+test для WPF/нативки.
2. **Зелёные аналайзеры ≠ корректный код.** Самый важный баг сессии (`MemoryPageCache`) компилировался и проходил аналайзеры — поймал только рантайм-тест. Логику проверять тестами на реальных сценариях, а «эвристики на догадках» избегать в пользу работы с реальными данными.

## 5. Параллельные волны агентов + кросс-платформенный гейт (PR #25–#27)

Три волны параллельных worktree-агентов (7 + 5 + inline) довели слой до alpha-DoD. Новые
системные уроки — об интеграции и о границах Linux-гейта:

1. **Worktree-агенты ветвятся от УСТАРЕВШЕЙ базы.** `isolation: worktree` ветвил агентов от
   `origin/main`, а не от HEAD рабочей ветки → они не видели незамерженную in-flight работу.
   Следствия: cherry-pick конфликты по общим файлам (`Foliant.sln`, `AppSettings.cs`,
   `IMPLEMENTATION_PLAN.md`) и регрессии на устаревшей базе — docs «исправили» (удалили) реальную
   фичу «обложка отдельно»; тест `SettingsViewModel` мокал старый `SaveAsync` вместо актуального
   `UpdateAsync`. **Правило:** считать базу агента потенциально устаревшей; интегрировать
   cherry-pick'ом дифа на актуальный HEAD и ПЕРЕпроверять тесты, а не мержить ветку агента целиком.
2. **Отчёт агента ≠ истина (trust-but-verify).** Агент заявил Domain coverage 100% (мерил
   `Domain.Tests` в изоляции); полный union по slnf дал 53 %. Всегда перемерять/перезапускать
   ИНТЕГРИРОВАННОЕ состояние, а не доверять изолированному отчёту субагента.
3. **Windows-only проекты не ловятся Linux-гейтом.** `Foliant.App`/`Foliant.UI` (net10.0-windows)
   вне `Foliant.CrossPlatform.slnf` → ошибки компиляции там всплывают ТОЛЬКО на Windows-CI:
   `AppHostBuilder` использовал `IHttpClientFactory` без `using System.Net.Http;` (CS0246) и прошёл
   все Linux-проверки + `verify-local.sh`. **Правило:** держать логику в кросс-платформенных слоях,
   а Windows-only проекты — тонкими; для кода там Windows-CI — единственная компиляционная проверка.
4. **Тонкости измерения покрытия.** (a) coverlet пишет один исходник под РАЗНЫМИ путями в разных
   тест-проектах (`Annotation.cs` / `Foliant.Domain/Annotation.cs` / `src/...`) → union обязан
   ключеваться по basename, иначе общий слой (Domain, на который ссылаются все) занижается вдвое.
   (b) `[ExcludeFromCodeCoverage]` НЕвалиден на interface/enum (CS0592) → declaration-only типы
   исключать через coverlet runsettings-фильтр (`tests/coverage.runsettings`). (c) покрытие общего
   слоя фрагментировано по тест-проектам; изолированный замер (`Domain.Tests` = 100 %) вводит в
   заблуждение — гейтить по полному union.
5. **Параллелизм окупается при непересекающихся файлах.** Реальное ускорение есть, НО общие файлы
   (`Foliant.sln`, `Directory.Packages.props`, resx, workflow-YAML) — горячие точки: назначать
   единоличного владельца на батч, сериализовать их правки; новые проекты регистрировать в
   sln/slnf централизованно при интеграции, а не в каждом worktree.
