using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfFormReader"/>: открывает PDF managed-кодом, итерирует
/// <c>AcroForm.Fields</c> и собирает значения text/checkbox/choice полей. Документ без
/// AcroForm возвращает пустой словарь.
/// </summary>
public sealed class PdfPigFormReader : IPdfFormReader
{
    private readonly ILogger<PdfPigFormReader> _log;

    public PdfPigFormReader(ILogger<PdfPigFormReader> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Form read is best-effort: corrupt PDF returns empty dictionary, never throws.")]
    public Task<IReadOnlyDictionary<string, string>> ReadAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        return Task.Run<IReadOnlyDictionary<string, string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = PdfPigDocument.Open(pdfPath);
                if (!doc.TryGetForm(out AcroForm? form) || form is null)
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in form.Fields)
                {
                    ct.ThrowIfCancellationRequested();
                    Collect(field, result);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to read AcroForm from '{Path}'; returning empty.", pdfPath);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }, ct);
    }

    private static void Collect(AcroFieldBase field, Dictionary<string, string> sink)
    {
        // PartialName может отсутствовать у промежуточного контейнера, тогда мы пропускаем сам
        // узел, но всё равно обходим children — Acrobat-style nested fields.
        string? name = field.Information?.PartialName;
        if (!string.IsNullOrEmpty(name))
        {
            sink[name] = ExtractValue(field);
        }

        // PdfPig'овский AcroFormExtensions.GetFields(AcroFieldBase) возвращает дочерние, но мы
        // не используем его здесь — поверхностная итерация по AcroForm.Fields уже включает
        // листья (PdfPig flatten'ит). Если в будущем потребуется иерархия — добавим обход children.
    }

    private static string ExtractValue(AcroFieldBase field) => field switch
    {
        AcroTextField t => t.Value ?? string.Empty,
        AcroCheckboxField c => c.IsChecked ? "Yes" : "Off",
        AcroComboBoxField cb => Join(cb.SelectedOptions),
        AcroListBoxField lb => Join(lb.SelectedOptions),
        // RadioButtons / Signature / Push button — value не маппится в простую строку
        // (radio: индекс выбранного, signature: бинарный hash). Возвращаем пустую строку.
        _ => string.Empty,
    };

    private static string Join(IReadOnlyList<string>? options) =>
        options is { Count: > 0 } ? string.Join(",", options) : string.Empty;
}
