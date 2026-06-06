using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Link-listing command wired to <see cref="IPdfLinkService"/> (read-only). The View opens the dialog
/// after <c>LoadLinks</c> has read the list into <see cref="CurrentLinks"/>. Same gate/suppress shape
/// as the fonts partial.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfLinkAnnotation> _currentLinks = [];

    /// <summary>Snapshot of link annotations loaded by the most recent <c>LoadLinksCommand</c>
    /// execution. Read-only; the dialog lists it.</summary>
    public IReadOnlyList<PdfLinkAnnotation> CurrentLinks => _currentLinks;

    /// <summary>Can the current document's links be listed: service present and source is a PDF.</summary>
    public bool CanListLinks =>
        _linkService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read link annotations via <see cref="IPdfLinkService.ListLinksAsync"/> into
    /// <see cref="CurrentLinks"/>. Best-effort.</summary>
    [RelayCommand(CanExecute = nameof(CanListLinks))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Link-list failure must not crash the tab.")]
    private async Task LoadLinksAsync(CancellationToken ct)
    {
        if (_linkService is null || !CanListLinks)
        {
            return;
        }

        try
        {
            var items = await _linkService.ListLinksAsync(_filePath, ct);
            _currentLinks.Clear();
            _currentLinks.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list links of '{Path}'.", _filePath);
        }
    }
}
