using Foliant.Domain;

namespace Foliant.Application.Services;

public interface IDocumentIndexer
{
    void Enqueue(IDocument document, string path);
}
