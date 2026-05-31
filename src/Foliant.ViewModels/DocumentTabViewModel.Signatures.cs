using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Read-only doorway in the tab VM to <see cref="ISignatureController"/> (Q-F25). UI opens
/// <c>SignaturesDialog</c>, which pulls a snapshot via <see cref="LoadDocumentSignatures"/>
/// and delegates per-signature validation to <see cref="ValidateSignatureAsync"/>.
///
/// PDFium reads the signature list eagerly when the controller is constructed, so
/// snapshots are cheap to take but not free — the dialog calls <see cref="LoadDocumentSignatures"/>
/// once on open instead of per-row binding.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>True iff the underlying document can produce a signature controller
    /// (PDF only — other engines return <c>null</c>). The dialog gates the Tools menu
    /// item on this so non-PDF tabs simply skip the entry.</summary>
    public bool CanViewSignatures =>
        Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Snapshot of <see cref="ISignatureController.Signatures"/>. Empty list when
    /// the document does not expose a controller (non-PDF) or has zero signatures.
    /// Safe to call repeatedly; each call rebuilds the controller (PDFium re-parses the file).</summary>
    public IReadOnlyList<DocumentSignature> LoadDocumentSignatures()
    {
        ISignatureController? controller = _document.GetSignatures();
        return controller?.Signatures ?? [];
    }

    /// <summary>Validate one signature via the document's controller. Returns <c>null</c>
    /// if the document does not have a controller — caller treats that as «no PDF» and
    /// surfaces an info banner instead of an error.</summary>
    public async Task<SignatureValidationResult?> ValidateSignatureAsync(
        DocumentSignature signature, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ISignatureController? controller = _document.GetSignatures();
        if (controller is null)
        {
            return null;
        }
        return await controller.ValidateAsync(signature, ct).ConfigureAwait(false);
    }
}
