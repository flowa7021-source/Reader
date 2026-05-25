using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// OCR-движок: преобразует растровый рендер страницы в <see cref="TextLayer"/>.
/// Реализации (PaddleOCR и т.п.) живут в <c>Foliant.Engines.Ocr</c>.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Версия движка/моделей. Используется в <see cref="CacheKey.EngineVersion"/>,
    /// чтобы автоматически инвалидировать кэш при апгрейде движка или моделей OCR.
    /// </summary>
    int Version { get; }

    Task<TextLayer> RecognizeAsync(
        IPageRender render,
        int pageIndex,
        OcrOptions options,
        CancellationToken ct);
}
