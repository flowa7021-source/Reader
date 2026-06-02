using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Foliant.Domain;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Pure, PDFium-free PAdES-B (Basic) signature validator. Validates, against the raw PDF bytes:
/// <list type="number">
/// <item>the CMS/PKCS#7 signature over the signed byte ranges (cryptographic integrity);</item>
/// <item>that <c>/ByteRange</c> covers the whole file except the <c>/Contents</c> window — i.e.
/// nothing was appended after signing (document integrity);</item>
/// <item>the signer certificate chains to a trusted root and is within its validity window
/// (<see cref="X509Chain"/>).</item>
/// </list>
/// Out of scope (PAdES T / LT / LTA — Phase 2 follow-up, Q-F26): TSA timestamp verification and
/// revocation (CRL/OCSP). Revocation is therefore explicitly <em>not</em> checked here.
/// </summary>
public static class PadesValidator
{
    /// <summary>
    /// Validates the first signature found in <paramref name="pdfBytes"/>. When
    /// <paramref name="extraTrustAnchor"/> is supplied it is added as an additional trust root
    /// (used by tests with a self-signed root, and by callers pinning an enterprise CA).
    /// </summary>
    public static SignatureValidationResult Validate(byte[] pdfBytes, X509Certificate2? extraTrustAnchor = null)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        PdfSignatureBytes? sig = ByteRangeParser.Parse(pdfBytes);
        if (sig is null)
        {
            return Failure("No parsable signature dictionary (/ByteRange + /Contents) found.");
        }

        byte[]? signedBytes = ByteRangeParser.AssembleSignedBytes(pdfBytes, sig);
        if (signedBytes is null)
        {
            return Failure("/ByteRange offsets fall outside the document bounds.");
        }

        bool untouched = IsByteRangeWholeFile(sig, pdfBytes.Length);
        return VerifyCms(sig.Pkcs7, signedBytes, untouched, extraTrustAnchor);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Untrusted PDF bytes — any decode/verify fault must degrade to IsValid=false, not crash the caller.")]
    private static SignatureValidationResult VerifyCms(
        byte[] pkcs7, byte[] signedBytes, bool untouched, X509Certificate2? extraTrustAnchor)
    {
        SignedCms cms = new(new ContentInfo(signedBytes), detached: true);
        try
        {
            cms.Decode(pkcs7);
        }
        catch (CryptographicException ex)
        {
            return Failure($"/Contents is not a valid PKCS#7 container: {ex.Message}");
        }

        try
        {
            // verifySignatureOnly: chain/trust is evaluated separately so a custom anchor can be
            // injected; here we only assert the digest matches the signed bytes.
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException)
        {
            return new SignatureValidationResult(
                IsValid: false,
                CertificateTrusted: false,
                DocumentUntouchedSinceSigning: untouched,
                FailureReason: "CMS signature does not match the signed bytes (document altered or wrong key).");
        }
        catch (Exception ex)
        {
            return Failure($"Unexpected error verifying signature: {ex.Message}");
        }

        X509Certificate2? signer = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
        (bool trusted, string? chainNote) = BuildChain(signer, extraTrustAnchor);

        return new SignatureValidationResult(
            IsValid: true,
            CertificateTrusted: trusted,
            DocumentUntouchedSinceSigning: untouched,
            FailureReason: trusted && untouched ? null : (chainNote ?? UntouchedNote(untouched)));
    }

    private static (bool Trusted, string? Note) BuildChain(X509Certificate2? signer, X509Certificate2? extraTrustAnchor)
    {
        if (signer is null)
        {
            return (false, "Signer certificate is absent from the PKCS#7 container.");
        }

        using X509Chain chain = new();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // PAdES-B: revocation out of scope.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (extraTrustAnchor is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(extraTrustAnchor);
        }

        bool ok = chain.Build(signer);
        return ok ? (true, null) : (false, DescribeChainFailure(chain));
    }

    private static string DescribeChainFailure(X509Chain chain)
    {
        foreach (X509ChainStatus status in chain.ChainStatus)
        {
            if (status.Status != X509ChainStatusFlags.NoError)
            {
                return $"Certificate chain not trusted: {status.StatusInformation.Trim()} ({status.Status}).";
            }
        }
        return "Certificate chain could not be built to a trusted root.";
    }

    // /ByteRange must reach EOF: if the second range ends before the file ends, an incremental
    // update was appended after signing and the signature no longer covers the whole document.
    private static bool IsByteRangeWholeFile(PdfSignatureBytes sig, int fileLength) =>
        sig.RangeOffset1 == 0 &&
        sig.RangeLength1 == sig.ContentsHoleStart &&
        sig.RangeOffset2 == sig.ContentsHoleEnd &&
        sig.RangeOffset2 + sig.RangeLength2 == fileLength;

    private static string UntouchedNote(bool untouched) =>
        untouched ? "Signature valid." : "Content was appended after the signature (/ByteRange stops before EOF).";

    private static SignatureValidationResult Failure(string reason) =>
        new(IsValid: false, CertificateTrusted: false, DocumentUntouchedSinceSigning: false, FailureReason: reason);
}
