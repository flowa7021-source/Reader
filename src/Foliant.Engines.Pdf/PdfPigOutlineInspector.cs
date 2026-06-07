using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает богатое /Outlines PDF через cos-уровень (<see cref="PdfOutlineCosReader"/>) — симметрично
/// rich-writer'у <see cref="PdfPigOutlineWriter"/>. Тонкий orchestrator: валидация аргумента +
/// offload в <see cref="Task.Run(Action)"/> + best-effort обёртка (битый / отсутствующий outline →
/// пустой список, не исключение). Mirror'ит форму <see cref="PdfPigOutlineReader"/>'а, но возвращает
/// <b>всё</b> дерево с rich-атрибутами (включая узлы с неразрешимой страницей — PageIndex = -1), а не
/// только page-bound записи для импорта закладок.
/// </summary>
public sealed class PdfPigOutlineInspector : IPdfOutlineInspector
{
    private readonly ILogger<PdfPigOutlineInspector> _log;

    /// <summary>Создаёт inspector.</summary>
    /// <param name="log">Логгер для предупреждений о нечитаемом outline'е.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> равен <see langword="null"/>.</exception>
    public PdfPigOutlineInspector(ILogger<PdfPigOutlineInspector> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Outline inspection is best-effort: any failure (corrupt PDF, missing Outlines) yields empty list, not a throw.")]
    public Task<IReadOnlyList<DocumentOutlineEntry>> ReadRichAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        return Task.Run<IReadOnlyList<DocumentOutlineEntry>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                byte[] source = File.ReadAllBytes(pdfPath);
                ct.ThrowIfCancellationRequested();
                return PdfOutlineCosReader.Read(source);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to read rich PDF outline from '{Path}'; returning empty.", pdfPath);
                return [];
            }
        }, ct);
    }
}
