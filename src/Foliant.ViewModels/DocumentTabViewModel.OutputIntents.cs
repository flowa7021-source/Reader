using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Output-intents listing wired to <see cref="IPdfOutputIntentService"/> (read-only). The document's
/// <c>/OutputIntents</c> declare the intended output colour rendering condition (PDF/X, PDF/A ICC
/// profiles) — print-production / preflight info. The View opens the dialog after
/// <c>LoadOutputIntents</c> has read the list into <see cref="CurrentOutputIntents"/>. Same
/// gate/suppress shape as the fonts/links partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfOutputIntent> _currentOutputIntents = [];

    /// <summary>Snapshot of output intents loaded by the most recent <c>LoadOutputIntentsCommand</c>
    /// execution. Read-only; the dialog lists it.</summary>
    public IReadOnlyList<PdfOutputIntent> CurrentOutputIntents => _currentOutputIntents;

    /// <summary>Can the current document's output intents be listed: service present and source is a PDF.</summary>
    public bool CanListOutputIntents =>
        _outputIntentService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read output intents via <see cref="IPdfOutputIntentService.ListAsync"/> into
    /// <see cref="CurrentOutputIntents"/>. Best-effort.</summary>
    [RelayCommand(CanExecute = nameof(CanListOutputIntents))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Output-intent list failure must not crash the tab.")]
    private async Task LoadOutputIntentsAsync(CancellationToken ct)
    {
        if (_outputIntentService is null || !CanListOutputIntents)
        {
            return;
        }

        try
        {
            var items = await _outputIntentService.ListAsync(_filePath, ct);
            _currentOutputIntents.Clear();
            _currentOutputIntents.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list output intents of '{Path}'.", _filePath);
        }
    }
}
