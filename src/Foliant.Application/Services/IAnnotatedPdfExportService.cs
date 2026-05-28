using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт PDF с встроенными <b>редактируемыми</b> аннотациями (Q-F17 Phase 2): читает исходный
/// PDF, добавляет настоящие annotation-объекты (<c>/Highlight</c>, <c>/Text</c>, <c>/Ink</c>) и
/// пишет новый PDF, который любой просмотрщик (Acrobat/Edge/Foxit) и показывает, и редактирует.
/// Реализация — на PDFium (нативный слой). Применимо только к PDF-источникам; sidecar остаётся
/// каноническим хранилищем — это экспорт, а не миграция.
/// </summary>
public interface IAnnotatedPdfExportService
{
    /// <summary>Прочитать <paramref name="sourcePdfPath"/>, встроить <paramref name="annotations"/>
    /// и атомарно записать результат в <paramref name="targetPath"/>. Невалидные аннотации
    /// пропускаются; пустой список даёт копию исходного PDF.</summary>
    Task ExportAsync(
        string sourcePdfPath,
        IReadOnlyList<Annotation> annotations,
        string targetPath,
        CancellationToken ct);
}
