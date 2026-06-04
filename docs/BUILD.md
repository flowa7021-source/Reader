# Build

> Сборка Foliant с нуля. Нужны Windows + .NET 10 SDK.

## Требования

| Что | Версия | Зачем |
|---|---|---|
| **Windows** | 10 21H2+ или 11 | Основная (и единственная в Phase 1–3) платформа |
| **.NET 10 SDK** | 10.0.x | LangVersion latest, NRT, Central Package Management |
| **PowerShell** | 7+ | Для `tools/fetch-natives.ps1` |
| **Git** | 2.40+ | LFS для тестовых ассетов |
| **Git LFS** | 3.x | `tests/assets/` |
| **Inno Setup** | 6.x | Сборка инсталлятора (опционально, только для release) |

## Первая сборка

```powershell
git clone https://github.com/flowa7021-source/Reader.git
cd Reader
git lfs install
git lfs pull

pwsh tools/fetch-natives.ps1                # tessdata модели по tier'у Basic

dotnet restore Foliant.sln --locked-mode
dotnet build  Foliant.sln -c Release -warnaserror
dotnet test   Foliant.sln -c Release --filter "Category!=Slow&Category!=E2E"
```

Первый `dotnet build` (или `dotnet test`) автоматически ставит Husky.Net (`dotnet tool restore`
+ `dotnet husky install`) через MSBuild-таргет в `Directory.Build.targets`. После этого
`git push` запускает hook `.husky/pre-push` (формат + build -warnaserror + быстрые тесты на
`Foliant.CrossPlatform.slnf`). Чтобы отключить авто-установку (например, в CI): `HUSKY=0
dotnet build …`. Подробнее — [CONTRIBUTING.md → «Установка pre-push hook»](../CONTRIBUTING.md#установка-pre-push-hook-huskynet).

## Тиры OCR-моделей

```powershell
pwsh tools/fetch-natives.ps1 -Tier Basic     # рус + eng (~30 МБ; default)
pwsh tools/fetch-natives.ps1 -Tier Standard  # + СНГ + базовая Европа (~150 МБ)
pwsh tools/fetch-natives.ps1 -Tier Full      # + CJK + арабский + иврит (~350 МБ)
```

`fetch-natives.ps1` идемпотентен: если SHA256 совпадает, ничего не качает.

## Запуск приложения

```powershell
dotnet run --project src/Foliant.App -c Release
# Smoke (без UI) — проверка композиции:
dotnet run --project src/Foliant.App -c Release -- --smoke
```

## Проверка перед PR

```powershell
dotnet format Foliant.sln --verify-no-changes
dotnet build  Foliant.sln -c Release -warnaserror
dotnet test   Foliant.sln -c Release --filter "Category!=Slow&Category!=E2E"
```

Меняли `Foliant.UI` / `Foliant.App` (или иной `net10.0-windows` проект) на Linux/macOS? Прогоните
их compile-проверку локально — поймает CS0108/CS1734/namespace-клэши и XAML-ошибки до Windows-CI:

```bash
dotnet build src/Foliant.UI/Foliant.UI.csproj   -c Release -warnaserror -p:EnableWindowsTargeting=true
dotnet build src/Foliant.App/Foliant.App.csproj  -c Release -warnaserror -p:EnableWindowsTargeting=true
```

## Performance

```powershell
# Quick (CI smoke)
dotnet run --project tests/Foliant.Performance -c Release -- --quick

# Full набор бенчмарков
dotnet run --project tests/Foliant.Performance -c Release

# Сравнить с baseline
dotnet run --project tools/perf-compare -c Release -- `
    --baseline tests/Foliant.Performance/baseline.json `
    --current  tests/Foliant.Performance/results `
    --threshold 15
```

!!! note "Где меряются baseline'ы"
    Нативные ключи (`OpenPdf500Pages`, `RenderPage1080p_Cold`, `RenderPage1080p_Warm`, `OcrPageRus`) зависят от PDFium/PaddleOCR и измеряются на **Windows perf-машине**; их значения в `baseline.json` пока **ориентировочные**. Кросс-платформенные бенчи (FTS / LRU / fingerprint / cachekey) гоняются на **Linux CI**. Ключ, отсутствующий в текущем прогоне, помечается `[skip]` и не считается регрессией.

## Релиз

Релиз триггерится тегом:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions `release.yml`:

1. Build + sign self-contained `Foliant.exe` через `signtool` (HSM/токен с EV cert).
2. Сборка 3 tier инсталляторов через `iscc.exe` (Inno Setup).
3. Подпись инсталляторов.
4. SHA256SUMS.
5. Создание GH Release с changelog.

Локально для проверки:

```powershell
dotnet publish src/Foliant.App -c Release -r win-x64 --self-contained -o publish/
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 /DTier=Basic installer/Foliant.Installer.InnoSetup/Foliant.iss
```

## Pro-репозиторий

Закрытый код Foliant.Pro.* — отдельный репозиторий, клонируется как submodule:

```powershell
git submodule add https://github.com/<your-org>/Foliant.Pro.git pro
git submodule update --init --recursive
```

`Foliant.App.csproj` детектит наличие `pro/` и подключает Pro-проекты автоматически через `<ItemGroup Condition="Exists('..\..\pro')">`.

## Кросс-платформенная разработка (Linux / macOS)

- **Тесты** проектов `Foliant.Domain`, `Foliant.Application`, **большинство** `Foliant.Infrastructure` — кросс-платформенные. Можно запускать на Linux / macOS.
- **WPF / Windows Forms** проекты (`Foliant.UI`, `Foliant.App`) — **запуск** только на Windows, но **компилируются** и на Linux/macOS флагом `-p:EnableWindowsTargeting=true` (подтягивает `Microsoft.WindowsDesktop.App.Ref`-пак). Это даёт compile-проверку C# code-behind **и** XAML-разметки (CS0108/CS1734, namespace-клэши, неверные типы в XAML) ещё до Windows-CI. Не покрывается локально: runtime data-binding (не ловится компиляцией ни на одной ОС) и фактический рендер/нативка.
- **PDFium / Tesseract integration tests** — формально cross-platform (PDFiumCore поддерживает Win/Linux/Mac), но в Phase 1 оптимизированы под Windows runner.

## Решение типичных проблем

| Симптом | Причина | Фикс |
|---|---|---|
| `WindowsBase.dll not found` / `*.dll not found` для UI/App на Linux | Нет windows-targeting пака | Добавить `-p:EnableWindowsTargeting=true` к `dotnet build` (compile-проверка; запуск — всё равно Windows) |
| `PDFiumCore native binary not found` | runtimes/ не скопировался | `dotnet build` (не только `restore`) |
| `Tesseract: tessdata not found` | `fetch-natives.ps1` не запускался | `pwsh tools/fetch-natives.ps1` |
| `dotnet format` ругается на свежий PR | EditorConfig нарушен | `dotnet format Foliant.sln` |
| SQLite-тесты падают на Linux в WSL | `journal_mode=WAL` несовместим с разделяемым FS | Не запускать SQLite-тесты в каталогах `\\wsl$\` |
