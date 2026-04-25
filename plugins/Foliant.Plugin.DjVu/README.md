# Foliant.Plugin.DjVu

Опциональный плагин для поддержки формата **DjVu** через DjVuLibre (out-of-process, per-call).

## Лицензия

GPL-2.0 у DjVuLibre. **Изоляция**: бинари DjVuLibre распространяются отдельным инсталлятором (`Foliant-Plugin-DjVu-Setup-{ver}.exe`, ~5 МБ), запускаются только как дочерний процесс. GPL не «заражает» ядро Foliant (которое MIT). См. `PROJECT_BOARD.md`, раздел 2.

## Реализация — спринт S9

Контракт:

```csharp
[Export(typeof(IEnginePlugin))]
internal sealed class DjVuEnginePlugin : IEnginePlugin { ... }

internal sealed class DjvuDocument : IDocument {
    // ddjvu для рендера, djvused для текстового слоя.
    // Каждый рендер: Process.Start("ddjvu", "-format=ppm -page=N input.djvu -")
    // Парсинг PPM stdout → BGRA32.
}
```

## Зависимости рантайма

- `ddjvu.exe`, `djvused.exe` из DjVuLibre 3.5.x (Windows build).
- Путь — из `HKCU\Software\Foliant\Plugins\DjVu = {InstallPath}`.
- При отсутствии — плагин не регистрируется в MEF; UI показывает «DjVu недоступно — установите плагин».

## Тесты — `tests/Foliant.Engines.*` (создадим в S9)

- 3 эталонных DjVu (book, scan, indirect).
- Открытие, рендер N страниц, OCR при необходимости.
- Покрытие: integration, не unit (out-of-process).
