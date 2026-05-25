using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Оркестратор OCR для всего документа. Выделен в порт, чтобы ViewModel-слой
/// можно было покрывать unit-тестами с подменой движка.
/// </summary>
public interface IOcrPipelineService
{
    /// <summary>
    /// Распознаёт все страницы документа, сообщая прогресс через
    /// <paramref name="progress"/>. Возвращает текстовые слои в порядке страниц.
    /// </summary>
    Task<IReadOnlyList<TextLayer>> RecognizeDocumentAsync(
        IDocument document,
        string docFingerprint,
        OcrOptions options,
        IProgress<OcrProgress>? progress,
        CancellationToken ct);
}
