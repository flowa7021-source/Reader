# Plugins

Foliant поддерживает **два типа** плагинов: in-process MEF (для Pro-функций) и out-of-process per-call (для GPL-зависимостей).

## 1. In-process Pro-плагины (MEF)

### Архитектура

Контракты — в открытом `Foliant.Plugins.Contracts` (MIT). Реализации — в закрытом `pro/Foliant.Pro.*` (proprietary). Загрузка через `System.Composition.Hosting` (Microsoft MEF reborn).

```csharp
[Export(typeof(IEnginePlugin))]
internal sealed class MyPdfEditorPlugin : IEnginePlugin
{
    public string Name    => "Foliant.Pro.AdvancedEditor";
    public string Version => "1.0.0";
    public DocumentKind Kind => DocumentKind.Pdf;
    public IDocumentLoader Loader => /* ... */;
}
```

### Discovery

`AssemblyCatalog` сканирует `%ProgramFiles%\Foliant\Plugins\`:

```csharp
var catalog = new ContainerConfiguration()
    .WithAssembliesInPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
var container = catalog.CreateContainer();
foreach (var p in container.GetExports<IEnginePlugin>()) { /* register in DI */ }
```

### Лицензирование Pro

Каждый Pro-плагин проверяет фичу при инициализации:

```csharp
if (!licenseManager.HasFeature("AdvancedEditor"))
{
    throw new ProFeatureNotLicensedException("AdvancedEditor");
}
```

UI отрисовывает Pro-фичи только если плагин зарегистрирован И licenseManager даёт OK.

## 2. Out-of-process плагины (per-call Process.Start)

### Зачем

GPL-лицензированные библиотеки (DjVuLibre) или большие headless-сервисы (LibreOffice). Out-of-process изоляция:

- GPL/MPL не «заражает» MIT-ядро — пользователь сам устанавливает плагин и явно соглашается с его лицензией.
- Краш плагина не валит UI.
- Простая модель: `Process.Start` на каждый вызов, без долгоживущих воркеров.

Overhead ~50–500 мс на вызов — приемлемо для редких операций (рендер DjVu, конвертация PDF→DOCX через LibreOffice).

### Установка

Отдельный installer (`Foliant-Plugin-DjVu-Setup-{ver}.exe`):
- Кладёт бинари в `%ProgramFiles%\Foliant\Plugins\DjVu\bin\`.
- Регистрирует путь в `HKCU\Software\Foliant\Plugins\DjVu`.
- При запуске Foliant обнаруживает плагин и регистрирует соответствующие `IDocumentLoader` / `IConverterPlugin`.

### Шаблон обвязки

```csharp
internal sealed class DjvuDocument : IDocument
{
    public async Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_ddjvuPath, $"-format=ppm -page={pageIndex + 1} {_path} -")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var bytes = await ReadAllAsync(proc.StandardOutput.BaseStream, ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0) throw new EngineException(...);
        return PpmDecoder.ToBgra32(bytes);
    }
}
```

### Тесты

- **Не unit**: реальный процесс. `Trait("Category", "Integration")`.
- 3 эталонных DjVu файла в `tests/assets/`.
- Smoke: rendering 5 страниц, validation размеров.

## 3. Карта плагинов в проекте

| Имя | Тип | Лицензия | Что даёт | Phase |
|---|---|---|---|---|
| `Foliant.Pro.AdvancedEditor` | In-process | proprietary | Reflow + font matching | 2–3 |
| `Foliant.Pro.Forms.Xfa` | In-process | proprietary | XFA с JS | 4 |
| `Foliant.Pro.Signatures` | In-process | proprietary | X.509 + PAdES B+T (LT/LTA в 4) | 2 |
| `Foliant.Pro.GostSignatures` | In-process | proprietary | КриптоПро / VipNet | 4+ |
| `Foliant.Pro.Redaction` | In-process | proprietary | Полное redaction | 3 |
| `Foliant.Pro.PdfA` | In-process | proprietary | PDF/A валидация (veraPDF) | 2 |
| `Foliant.Pro.Convert` | In-process | proprietary | High-fidelity конвертеры | 3 |
| `Foliant.Pro.Batch` | In-process | proprietary | Batch processing | 3 |
| `Foliant.Plugin.DjVu` | Out-of-process | GPL-2.0 (изолирован) | DjVu рендер/OCR/правка | 1 |
| `Foliant.Plugin.LibreOffice` | Out-of-process | MPL-2.0 + LGPL (изолирован) | High-fidelity DOCX/XLSX | 3 |

## 4. Что **не** делать

- **Не** добавлять GPL-зависимость в основные `src/Foliant.*` проекты. Только через отдельный плагин-инсталлятор.
- **Не** делать persistent worker для out-of-process плагинов в Phase 1. Простота → отладка → надёжность.
- **Не** скрывать наличие плагина: UI должен явно показывать «DjVu доступно» / «DjVu не установлено — установить?».
