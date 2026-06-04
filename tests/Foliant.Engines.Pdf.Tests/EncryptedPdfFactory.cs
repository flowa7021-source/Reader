using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Генерирует минимальный PDF, зашифрованный стандартным security handler'ом (Standard,
/// V=1 / R=2, RC4-40) с НЕпустым user-паролем. Используется только в тестах: даёт реальный
/// зашифрованный байт-поток, на котором PDFium возвращает <c>FPDF_ERR_PASSWORD</c> при попытке
/// открыть без пароля. Своей крипты в проде НЕТ (write-encryption — отдельный трек, остаётся
/// stub) — RC4 здесь нужен исключительно чтобы воспроизвести «нужен пароль» на engine-уровне.
///
/// <para>Алгоритм — ISO 32000-1 §7.6.3 (Algorithm 2/3/4/5) для R=2. RC4 — устаревший шифр,
/// поэтому реализован вручную (BCL его не экспонирует) и применяется только к тестовому
/// фикстуру, не к пользовательским данным.</para>
/// </summary>
internal static class EncryptedPdfFactory
{
    // 32-байтовая padding-строка из спецификации (Algorithm 2, шаг a).
    private static readonly byte[] Pad =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>
    /// Build an RC4-40 (R=2) encrypted PDF whose USER password is "test" and owner password is
    /// empty. Opening it with <c>null</c>/empty password fails → PDFium reports FPDF_ERR_PASSWORD.
    /// </summary>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "PDF Standard security handler R=2 mandates MD5+RC4 by ISO 32000-1 §7.6.3. " +
                        "Used only to build a test fixture that reproduces «password required» in PDFium; " +
                        "no user data is protected with it.")]
    public static byte[] CreateRc4WithUserPassword()
    {
        var enc = Encoding.Latin1;
        byte[] id = RandomNumberGenerator.GetBytes(16); // /ID[0] — file identifier
        byte[] userPwd = enc.GetBytes("test");
        const int p = -44; // permissions bitmask (all-deny-ish; value is irrelevant for the test)
        const int keyLenBytes = 5; // 40-bit

        byte[] paddedUser = PadPassword(userPwd);
        byte[] paddedOwner = PadPassword([]); // empty owner password

        // Algorithm 3: O entry. RC4(paddedUser) keyed by MD5(paddedOwner)[..5].
        byte[] ownerKey = MD5.HashData(paddedOwner)[..keyLenBytes];
        byte[] o = Rc4(ownerKey, paddedUser);

        // Algorithm 2: encryption key = MD5(paddedUser + O + P(LE32) + ID)[..5].
        byte[] keyMaterial = [.. paddedUser, .. o, .. BitConverter.GetBytes(p), .. id];
        byte[] fileKey = MD5.HashData(keyMaterial)[..keyLenBytes];

        // Algorithm 4 (R=2): U entry = RC4(Pad) keyed by fileKey.
        byte[] u = Rc4(fileKey, Pad);

        return Assemble(enc, id, o, u, p, keyLenBytes, fileKey);
    }

    private static byte[] PadPassword(byte[] password)
    {
        byte[] result = new byte[32];
        int take = Math.Min(password.Length, 32);
        Array.Copy(password, result, take);
        Array.Copy(Pad, 0, result, take, 32 - take);
        return result;
    }

    // Per-object key (Algorithm 1): MD5(fileKey + obj#(LE24) + gen#(LE16))[..min(keyLen+5,16)].
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "PDF Algorithm 1 (ISO 32000-1 §7.6.2) mandates MD5 for the per-object key; test-only fixture.")]
    private static byte[] ObjectKey(byte[] fileKey, int objNumber, int keyLenBytes)
    {
        byte[] ext =
        [
            .. fileKey,
            (byte)(objNumber & 0xFF), (byte)((objNumber >> 8) & 0xFF), (byte)((objNumber >> 16) & 0xFF),
            0x00, 0x00, // generation 0
        ];
        return MD5.HashData(ext)[..Math.Min(keyLenBytes + 5, 16)];
    }

    // Textbook RC4 (KSA + PRGA). Symmetric — same routine encrypts and decrypts.
    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            s[i] = (byte)i;
        }

        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[data.Length];
        for (int i = 0, x = 0, y = 0; i < data.Length; i++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            output[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 0xFF]);
        }

        return output;
    }

    private static byte[] Assemble(
        Encoding enc, byte[] id, byte[] o, byte[] u, int p, int keyLenBytes, byte[] fileKey)
    {
        // Encrypt the single page's (empty) content stream with its object key. The stream is
        // empty, so the ciphertext is empty too — but the /Encrypt dict still forces PDFium to
        // demand a password, which is all the test needs.
        byte[] contentKey = ObjectKey(fileKey, 4, keyLenBytes);
        byte[] content = Rc4(contentKey, []);

        string header = "%PDF-1.4\n";
        string obj1 = "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n";
        string obj2 = "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n";
        string obj3 = "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R>>\nendobj\n";
        string obj4 =
            $"4 0 obj\n<</Length {content.Length}>>\nstream\n" +
            enc.GetString(content) + "\nendstream\nendobj\n";
        string obj5 =
            "5 0 obj\n<</Filter/Standard/V 1/R 2/O <" + Hex(o) + ">/U <" + Hex(u) +
            $">/P {p}>>\nendobj\n";

        int off1 = enc.GetByteCount(header);
        int off2 = off1 + enc.GetByteCount(obj1);
        int off3 = off2 + enc.GetByteCount(obj2);
        int off4 = off3 + enc.GetByteCount(obj3);
        int off5 = off4 + enc.GetByteCount(obj4);
        int xrefStart = off5 + enc.GetByteCount(obj5);

        string xref =
            "xref\n0 6\n0000000000 65535 f \n" +
            $"{off1:D10} 00000 n \n{off2:D10} 00000 n \n{off3:D10} 00000 n \n" +
            $"{off4:D10} 00000 n \n{off5:D10} 00000 n \n";

        string idHex = Hex(id);
        string trailer =
            "trailer\n<</Size 6/Root 1 0 R/Encrypt 5 0 R/ID[<" + idHex + "><" + idHex + ">]>>\n" +
            $"startxref\n{xrefStart}\n%%EOF\n";

        return enc.GetBytes(header + obj1 + obj2 + obj3 + obj4 + obj5 + xref + trailer);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
