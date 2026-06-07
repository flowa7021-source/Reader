using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Rich outline («Table of Contents») inspection wired to <see cref="IPdfOutlineInspector"/>
/// (read-only). Unlike the bookmark-import reader, this surfaces the full <c>/Outlines</c> richness —
/// nesting depth, destination zoom mode, bold/italic, colour, open/closed — for a read-only viewer.
/// The View opens the dialog after <c>LoadOutline</c> has read the tree into
/// <see cref="CurrentOutline"/>. Same gate/suppress shape as the fonts/links partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<DocumentOutlineEntry> _currentOutline = [];

    /// <summary>Snapshot of the rich outline loaded by the most recent <c>LoadOutlineCommand</c>
    /// execution (pre-order, depth-tagged). Read-only; the viewer dialog lists it.</summary>
    public IReadOnlyList<DocumentOutlineEntry> CurrentOutline => _currentOutline;

    /// <summary>Can the current document's outline be inspected: inspector present and source is a PDF.</summary>
    public bool CanViewOutline =>
        _outlineInspector is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read the rich outline via <see cref="IPdfOutlineInspector.ReadRichAsync"/> into
    /// <see cref="CurrentOutline"/>. Best-effort: a corrupt/absent <c>/Outlines</c> leaves the snapshot
    /// empty (the viewer then shows the «no outline» hint) instead of crashing the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanViewOutline))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Outline-read failure must not crash the tab.")]
    private async Task LoadOutlineAsync(CancellationToken ct)
    {
        if (_outlineInspector is null || !CanViewOutline)
        {
            return;
        }

        try
        {
            var items = await _outlineInspector.ReadRichAsync(_filePath, ct);
            _currentOutline.Clear();
            _currentOutline.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read outline of '{Path}'.", _filePath);
        }
    }
}
