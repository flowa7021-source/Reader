using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Embedded-file attachment commands wired to <see cref="IPdfAttachmentService"/>. The View opens the
/// «Attachments» dialog after <c>LoadAttachments</c> has read the current list into
/// <see cref="CurrentAttachments"/>; the dialog returns a single action (Add / Extract / Remove) and
/// the View forwards it to the matching command with paths it picked via file dialogs. Same
/// gate/suppress shape as the page-label / OCG partials. Add/Remove produce a new PDF (original never
/// mutated); Extract writes the decoded payload to a file.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfAttachment> _currentAttachments = [];

    /// <summary>Snapshot of attachments loaded by the most recent <c>LoadAttachmentsCommand</c>
    /// execution. Exposed read-only so the dialog can list them.</summary>
    public IReadOnlyList<PdfAttachment> CurrentAttachments => _currentAttachments;

    /// <summary>Can the current document's attachments be managed: service present and source is a
    /// PDF. Same gate shape as <see cref="CanEditPageLabels"/>.</summary>
    public bool CanManageAttachments =>
        _attachmentService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read the embedded-file list via <see cref="IPdfAttachmentService.ListAsync"/> into
    /// <see cref="CurrentAttachments"/>. Best-effort: a corrupt name tree leaves the snapshot empty.</summary>
    [RelayCommand(CanExecute = nameof(CanManageAttachments))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attachment-list failure must not crash the tab.")]
    private async Task LoadAttachmentsAsync(CancellationToken ct)
    {
        if (_attachmentService is null || !CanManageAttachments)
        {
            return;
        }

        try
        {
            var items = await _attachmentService.ListAsync(_filePath, ct);
            _currentAttachments.Clear();
            _currentAttachments.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list attachments of '{Path}'.", _filePath);
        }
    }

    /// <summary>Embed <see cref="AddAttachmentRequest.FileToAttachPath"/> into a new PDF at
    /// <see cref="AddAttachmentRequest.TargetPath"/>. No-op when the service is absent, the source is
    /// not a PDF, or required paths are blank.</summary>
    [RelayCommand(CanExecute = nameof(CanManageAttachments))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attachment-add failure must not crash the tab.")]
    private async Task AddAttachmentAsync(AddAttachmentRequest? request, CancellationToken ct)
    {
        if (_attachmentService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.FileToAttachPath)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanManageAttachments)
        {
            return;
        }

        try
        {
            await _attachmentService.AddAsync(_filePath, request.TargetPath, request.FileToAttachPath, request.Description, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add attachment to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Extract <see cref="ExtractAttachmentRequest.Name"/>'s decoded bytes to
    /// <see cref="ExtractAttachmentRequest.TargetFilePath"/>. No-op on missing service / blank paths.</summary>
    [RelayCommand(CanExecute = nameof(CanManageAttachments))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attachment-extract failure must not crash the tab.")]
    private async Task ExtractAttachmentAsync(ExtractAttachmentRequest? request, CancellationToken ct)
    {
        if (_attachmentService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.TargetFilePath)
            || !CanManageAttachments)
        {
            return;
        }

        try
        {
            await _attachmentService.ExtractAsync(_filePath, request.Name, request.TargetFilePath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract attachment '{Name}' to '{Path}'.", request.Name, request.TargetFilePath);
        }
    }

    /// <summary>Remove <see cref="RemoveAttachmentRequest.Name"/> from a new PDF at
    /// <see cref="RemoveAttachmentRequest.TargetPath"/>. No-op on missing service / blank paths.</summary>
    [RelayCommand(CanExecute = nameof(CanManageAttachments))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attachment-remove failure must not crash the tab.")]
    private async Task RemoveAttachmentAsync(RemoveAttachmentRequest? request, CancellationToken ct)
    {
        if (_attachmentService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanManageAttachments)
        {
            return;
        }

        try
        {
            await _attachmentService.RemoveAsync(_filePath, request.TargetPath, request.Name, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove attachment '{Name}' from '{Path}'.", request.Name, request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>AddAttachmentCommand</c>: the file to embed + target PDF
/// path (Save-As) + optional description. UI-flow concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record AddAttachmentRequest(string FileToAttachPath, string TargetPath, string? Description);

/// <summary>View-supplied envelope for <c>ExtractAttachmentCommand</c>: attachment name + the file
/// path to write the decoded payload to.</summary>
public sealed record ExtractAttachmentRequest(string Name, string TargetFilePath);

/// <summary>View-supplied envelope for <c>RemoveAttachmentCommand</c>: attachment name + target PDF
/// path (Save-As).</summary>
public sealed record RemoveAttachmentRequest(string Name, string TargetPath);
