using Foliant.Plugins.Contracts;

namespace Foliant.Infrastructure.Plugins;

/// <summary>
/// Обнаруживает engine-плагины (<see cref="IEnginePlugin"/>), экспортированные через MEF2
/// (<c>[Export(typeof(IEnginePlugin))]</c>), из каталога сборок. Composition-root (AppHostBuilder)
/// регистрирует <see cref="IEnginePlugin.Loader"/> каждого найденного плагина как
/// <c>IDocumentLoader</c>, давая drop-in поддержку форматов (DjVu и будущие) без правки ядра.
///
/// Best-effort: отсутствующий/пустой каталог → пусто; битая сборка → пропуск (лог), не падение.
/// </summary>
public interface IPluginCatalog
{
    /// <summary>Загрузить и скомпоновать все плагины из <paramref name="pluginsDirectory"/>.
    /// Возвращает пустой список, если каталога нет или в нём нет валидных экспортов.</summary>
    IReadOnlyList<IEnginePlugin> Discover(string pluginsDirectory);
}
