using Foliant.Domain;

namespace Foliant.Plugins.Contracts;

/// <summary>
/// Контракт плагина-движка документа (PDF, DjVu, EPUB, ...).
/// Реализации регистрируются в MEF через [Export(typeof(IEnginePlugin))].
/// </summary>
public interface IEnginePlugin
{
    string Name { get; }

    string Version { get; }

    DocumentKind Kind { get; }

    IDocumentLoader Loader { get; }
}
