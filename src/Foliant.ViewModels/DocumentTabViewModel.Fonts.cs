using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Font-listing command wired to <see cref="IPdfFontService"/> (Acrobat Document Properties → Fonts).
/// Read-only: the View opens the dialog after <c>LoadFonts</c> has read the list into
/// <see cref="CurrentFonts"/>. Same gate/suppress shape as the other inspection partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfFontInfo> _currentFonts = [];

    /// <summary>Snapshot of fonts loaded by the most recent <c>LoadFontsCommand</c> execution.
    /// Read-only; the dialog lists it.</summary>
    public IReadOnlyList<PdfFontInfo> CurrentFonts => _currentFonts;

    /// <summary>Can the current document's fonts be listed: service present and source is a PDF.</summary>
    public bool CanListFonts =>
        _fontService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read the font list via <see cref="IPdfFontService.ListFontsAsync"/> into
    /// <see cref="CurrentFonts"/>. Best-effort.</summary>
    [RelayCommand(CanExecute = nameof(CanListFonts))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Font-list failure must not crash the tab.")]
    private async Task LoadFontsAsync(CancellationToken ct)
    {
        if (_fontService is null || !CanListFonts)
        {
            return;
        }

        try
        {
            var items = await _fontService.ListFontsAsync(_filePath, ct);
            _currentFonts.Clear();
            _currentFonts.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list fonts of '{Path}'.", _filePath);
        }
    }
}
