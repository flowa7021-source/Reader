namespace Foliant.Domain;

/// <summary>
/// Единая абстракция документа: PDF, DjVu, изображение, EPUB, FB2, MOBI.
/// UI и ViewModel не знают, с чем работают.
/// </summary>
public interface IDocument : IAsyncDisposable
{
    DocumentKind Kind { get; }

    int PageCount { get; }

    DocumentMetadata Metadata { get; }

    PageSize GetPageSize(int pageIndex);

    Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct);

    Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct);

    /// <summary>Null для read-only документов (EPUB/FB2/MOBI/большинства DjVu).</summary>
    IDocumentEditor? GetEditor();

    /// <summary>Null если в документе нет форм AcroForm/XFA.</summary>
    IFormController? GetForms();

    /// <summary>Null если в документе нет цифровых подписей.</summary>
    ISignatureController? GetSignatures();
}
