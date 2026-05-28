using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Composition.Hosting;
using Foliant.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Plugins;

/// <summary>
/// MEF2-реализация <see cref="IPluginCatalog"/>. Грузит <c>*.dll</c> из каталога в default
/// <see cref="AssemblyLoadContext"/> (зависимости плагина — <c>Foliant.Domain</c>/
/// <c>Foliant.Plugins.Contracts</c> — уже загружены приложением, поэтому резолвятся), затем
/// компонует контейнер <see cref="ContainerConfiguration.WithAssemblies(IEnumerable{Assembly})"/>
/// и забирает экспорты <see cref="IEnginePlugin"/>.
///
/// System.Composition (MEF2) не имеет <c>DirectoryCatalog</c> — поэтому каталог сканируется
/// вручную, а композиция строится из списка успешно загруженных сборок.
/// </summary>
public sealed class PluginCatalog : IPluginCatalog
{
    private readonly ILogger<PluginCatalog> _log;

    public PluginCatalog(ILogger<PluginCatalog> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public IReadOnlyList<IEnginePlugin> Discover(string pluginsDirectory)
    {
        ArgumentNullException.ThrowIfNull(pluginsDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            _log.LogInformation("Plugin directory '{Dir}' does not exist; no plugins loaded.", pluginsDirectory);
            return [];
        }

        var assemblies = new List<Assembly>();
        foreach (string dll in Directory.EnumerateFiles(pluginsDirectory, "*.dll"))
        {
            if (TryLoad(dll) is { } asm)
            {
                assemblies.Add(asm);
            }
        }

        if (assemblies.Count == 0)
        {
            return [];
        }

        return Compose(assemblies);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A broken/incompatible plugin DLL must be skipped, not crash host startup.")]
    private Assembly? TryLoad(string dllPath)
    {
        try
        {
            // LoadFromAssemblyPath в default-контексте: зависимости плагина, уже загруженные
            // приложением (Domain/Contracts), переиспользуются — без дублирования типов.
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load candidate plugin assembly '{Dll}'; skipping.", dllPath);
            return null;
        }
    }

    /// <summary>Скомпоновать <see cref="IEnginePlugin"/>-экспорты из набора сборок. Выделено для
    /// unit-тестирования без файловой системы.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Composition failure (bad export metadata) must yield empty, not crash startup.")]
    internal IReadOnlyList<IEnginePlugin> Compose(IReadOnlyList<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        try
        {
            var configuration = new ContainerConfiguration().WithAssemblies(assemblies);
            using CompositionHost container = configuration.CreateContainer();
            var plugins = container.GetExports<IEnginePlugin>().ToList();
            _log.LogInformation("Discovered {Count} engine plugin(s): {Names}.",
                plugins.Count, string.Join(", ", plugins.Select(p => p.Name)));
            return plugins;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Plugin composition failed; continuing with no plugins.");
            return [];
        }
    }
}
