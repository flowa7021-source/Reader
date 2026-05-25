using System.Composition;
using Foliant.Domain;
using Foliant.Plugins.Contracts;

namespace Foliant.Plugin.DjVu;

/// <summary>
/// MEF entry point for the DjVu engine. Discovered via
/// <c>[Export(typeof(IEnginePlugin))]</c>; supplies the out-of-process loader.
/// </summary>
[Export(typeof(IEnginePlugin))]
public sealed class DjVuEnginePlugin : IEnginePlugin
{
    public string Name => "DjVu (DjVuLibre)";

    public string Version => "0.1.0";

    public DocumentKind Kind => DocumentKind.Djvu;

    public IDocumentLoader Loader { get; } = new DjvuDocumentLoader();
}
