namespace Foliant.Domain;

public interface ISignatureController
{
    IReadOnlyList<DocumentSignature> Signatures { get; }

    Task<SignatureValidationResult> ValidateAsync(
        DocumentSignature signature,
        CancellationToken ct);
}

public sealed record DocumentSignature(
    string SignerName,
    DateTimeOffset SignedAt,
    string? Reason,
    string? Location,
    SignatureKind Kind);

public enum SignatureKind
{
    PadesB,
    PadesT,
    PadesLT,
    PadesLTA,
    Cms,
    Gost,
}

public sealed record SignatureValidationResult(
    bool IsValid,
    bool CertificateTrusted,
    bool DocumentUntouchedSinceSigning,
    string? FailureReason);
