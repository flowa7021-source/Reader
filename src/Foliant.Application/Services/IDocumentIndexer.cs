using Foliant.Domain;

namespace Foliant.Application.Services;

public interface IDocumentIndexer
{
    void Enqueue(IDocument document, string path);

    /// <summary>Проиндексировать заранее вычисленные текстовые слои (например, результат OCR
    /// для скана, у которого нет собственного текстового слоя) — чтобы они стали искомыми.</summary>
    void EnqueueLayers(string path, IReadOnlyList<TextLayer> layers);
}
