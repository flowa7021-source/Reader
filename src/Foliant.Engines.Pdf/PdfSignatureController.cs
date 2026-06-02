using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Read-only PDFium-обёртка над цифровыми подписями PDF (Q-F25). Eagerly загружает
/// список подписей из документа на construction и кэширует. <see cref="ValidateAsync"/>
/// делегирует в <see cref="PadesValidator"/> реальную PAdES-B криптовалидацию (CMS verify +
/// ByteRange integrity + cert chain). TSA timestamp / revocation (PAdES T/LT/LTA) — Phase 2+
/// (Q-F26).
///
/// SignerName парсится из PKCS#7 SignedData blob'а через in-box
/// <see cref="SignedCms"/> — CN из Subject первого SignerInfo certificate.
///
/// SignatureKind определяется по SubFilter (PDF /SubFilter entry):
/// <list type="bullet">
/// <item>adbe.pkcs7.* → <see cref="SignatureKind.Cms"/></item>
/// <item>ETSI.CAdES.* → <see cref="SignatureKind.PadesB"/></item>
/// <item>ETSI.RFC3161 → <see cref="SignatureKind.PadesT"/></item>
/// <item>иначе → <see cref="SignatureKind.Cms"/> (catch-all)</item>
/// </list>
/// </summary>
public sealed class PdfSignatureController : ISignatureController
{
    private static readonly Lock NativeGate = new();
    private readonly string _pdfPath;

    public IReadOnlyList<DocumentSignature> Signatures { get; }

    public PdfSignatureController(string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        _pdfPath = pdfPath;
        Signatures = ReadSignatures(pdfPath);
    }

    /// <summary>PAdES-B validation delegated to <see cref="PadesValidator"/> (CMS verify +
    /// ByteRange integrity + cert chain). T-level / revocation remain Phase 2 (Q-F26).</summary>
    public async Task<SignatureValidationResult> ValidateAsync(DocumentSignature signature, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signature);
        byte[] bytes = await File.ReadAllBytesAsync(_pdfPath, ct).ConfigureAwait(false);
        return PadesValidator.Validate(bytes);
    }

    private static List<DocumentSignature> ReadSignatures(string pdfPath)
    {
        lock (NativeGate)
        {
            PdfLibrary.EnsureInitialized();

            byte[] bytes = File.ReadAllBytes(pdfPath);
            GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
                if (doc is null)
                {
                    return [];
                }

                try
                {
                    int count = fpdf_signature.FPDF_GetSignatureCount(doc);
                    if (count <= 0)
                    {
                        return [];
                    }

                    var list = new List<DocumentSignature>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var sig = fpdf_signature.FPDF_GetSignatureObject(doc, i);
                        if (sig is null)
                        {
                            continue;
                        }
                        list.Add(ReadOne(sig));
                    }
                    return list;
                }
                finally
                {
                    fpdfview.FPDF_CloseDocument(doc);
                }
            }
            finally
            {
                pin.Free();
            }
        }
    }

    private static DocumentSignature ReadOne(FpdfSignatureT sig)
    {
        byte[] contents = ReadContents(sig);
        string subFilter = ReadSubFilter(sig);
        string reasonUtf16 = ReadReason(sig);
        string timeAscii = ReadTime(sig);

        string signerName = ExtractSignerName(contents);
        DateTimeOffset signedAt = ParsePdfDate(timeAscii);
        SignatureKind kind = MapKindFromSubFilter(subFilter);

        return new DocumentSignature(
            SignerName: signerName,
            SignedAt: signedAt,
            Reason: string.IsNullOrWhiteSpace(reasonUtf16) ? null : reasonUtf16,
            Location: null,
            Kind: kind);
    }

    private static byte[] ReadContents(FpdfSignatureT sig)
    {
        ulong needed = fpdf_signature.FPDFSignatureObjGetContents(sig, IntPtr.Zero, 0);
        if (needed == 0)
        {
            return [];
        }
        byte[] buffer = new byte[needed];
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            ulong written = fpdf_signature.FPDFSignatureObjGetContents(sig, pin.AddrOfPinnedObject(), needed);
            return written == 0 ? [] : buffer;
        }
        finally
        {
            pin.Free();
        }
    }

    private static unsafe string ReadSubFilter(FpdfSignatureT sig)
    {
        ulong needed = fpdf_signature.FPDFSignatureObjGetSubFilter(sig, null, 0);
        if (needed == 0)
        {
            return string.Empty;
        }
        sbyte[] buffer = new sbyte[needed];
        fixed (sbyte* ptr = buffer)
        {
            ulong written = fpdf_signature.FPDFSignatureObjGetSubFilter(sig, ptr, needed);
            if (written == 0)
            {
                return string.Empty;
            }
            int len = (int)written;
            if (len > 0 && buffer[len - 1] == 0)
            {
                len--;
            }
            return Encoding.ASCII.GetString((byte*)ptr, len);
        }
    }

    private static unsafe string ReadTime(FpdfSignatureT sig)
    {
        ulong needed = fpdf_signature.FPDFSignatureObjGetTime(sig, null, 0);
        if (needed == 0)
        {
            return string.Empty;
        }
        sbyte[] buffer = new sbyte[needed];
        fixed (sbyte* ptr = buffer)
        {
            ulong written = fpdf_signature.FPDFSignatureObjGetTime(sig, ptr, needed);
            if (written == 0)
            {
                return string.Empty;
            }
            int len = (int)written;
            if (len > 0 && buffer[len - 1] == 0)
            {
                len--;
            }
            return Encoding.ASCII.GetString((byte*)ptr, len);
        }
    }

    private static string ReadReason(FpdfSignatureT sig)
    {
        ulong needed = fpdf_signature.FPDFSignatureObjGetReason(sig, IntPtr.Zero, 0);
        if (needed <= 2)
        {
            return string.Empty;
        }
        byte[] buffer = new byte[needed];
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            ulong written = fpdf_signature.FPDFSignatureObjGetReason(sig, pin.AddrOfPinnedObject(), needed);
            if (written < 2)
            {
                return string.Empty;
            }
            int byteCount = (int)written;
            // Strip trailing NUL (2 bytes UTF-16).
            if (byteCount >= 2 && buffer[byteCount - 1] == 0 && buffer[byteCount - 2] == 0)
            {
                byteCount -= 2;
            }
            return byteCount > 0 ? Encoding.Unicode.GetString(buffer, 0, byteCount) : string.Empty;
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>Декодирует PKCS#7 SignedData, достаёт Subject первого SignerInfo certificate,
    /// извлекает CN. Возвращает <c>"(unknown)"</c>, если контейнер невалиден или CN не найден.
    /// Public для unit-тестирования без I/O.</summary>
    public static string ExtractSignerName(byte[] pkcs7Contents)
    {
        ArgumentNullException.ThrowIfNull(pkcs7Contents);
        if (pkcs7Contents.Length == 0)
        {
            return "(unknown)";
        }

        try
        {
            var cms = new SignedCms();
            cms.Decode(pkcs7Contents);
            if (cms.SignerInfos.Count == 0)
            {
                return "(unknown)";
            }
            X509Certificate2? cert = cms.SignerInfos[0].Certificate;
            if (cert is null)
            {
                return "(unknown)";
            }
            // GetNameInfo(SimpleName) даёт CN из Subject (если есть) или email иначе.
            string name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(name) ? cert.Subject : name;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return "(unknown)";
        }
    }

    /// <summary>PDF /M date format: <c>D:YYYYMMDDHHmmSSOHH'mm'</c> (Adobe). Возвращает
    /// <see cref="DateTimeOffset.MinValue"/> если не парсится. Public для unit-тестов.</summary>
    public static DateTimeOffset ParsePdfDate(string pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate))
        {
            return DateTimeOffset.MinValue;
        }

        string s = pdfDate.StartsWith("D:", StringComparison.Ordinal) ? pdfDate[2..] : pdfDate;

        if (s.Length < 4 || !int.TryParse(s.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            return DateTimeOffset.MinValue;
        }

        int month = 1, day = 1, hour = 0, minute = 0, second = 0;
        if (s.Length >= 6)
        {
            int.TryParse(s.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month);
        }
        if (s.Length >= 8)
        {
            int.TryParse(s.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day);
        }
        if (s.Length >= 10)
        {
            int.TryParse(s.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out hour);
        }
        if (s.Length >= 12)
        {
            int.TryParse(s.AsSpan(10, 2), NumberStyles.None, CultureInfo.InvariantCulture, out minute);
        }
        if (s.Length >= 14)
        {
            int.TryParse(s.AsSpan(12, 2), NumberStyles.None, CultureInfo.InvariantCulture, out second);
        }

        DateTime dt;
        try
        {
            dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MinValue;
        }

        TimeSpan offset = TimeSpan.Zero;
        if (s.Length >= 15 && (s[14] == '+' || s[14] == '-'))
        {
            char sign = s[14];
            if (s.Length >= 17 &&
                int.TryParse(s.AsSpan(15, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int offHours))
            {
                int offMinutes = 0;
                if (s.Length >= 20 && s[17] == '\'')
                {
                    int.TryParse(s.AsSpan(18, 2), NumberStyles.None, CultureInfo.InvariantCulture, out offMinutes);
                }
                offset = new TimeSpan(offHours, offMinutes, 0);
                if (sign == '-')
                {
                    offset = -offset;
                }
            }
            // 'Z' / unknown signs leave offset at zero.
        }

        return new DateTimeOffset(dt, offset);
    }

    /// <summary>SubFilter → <see cref="SignatureKind"/>. ASCII-сравнение, case-insensitive.</summary>
    public static SignatureKind MapKindFromSubFilter(string subFilter)
    {
        ArgumentNullException.ThrowIfNull(subFilter);
        if (subFilter.StartsWith("ETSI.CAdES", StringComparison.OrdinalIgnoreCase))
        {
            return SignatureKind.PadesB;
        }
        if (subFilter.StartsWith("ETSI.RFC3161", StringComparison.OrdinalIgnoreCase))
        {
            return SignatureKind.PadesT;
        }
        return SignatureKind.Cms;
    }
}
