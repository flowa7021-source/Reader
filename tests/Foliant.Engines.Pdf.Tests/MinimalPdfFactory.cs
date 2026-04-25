using System.Text;

namespace Foliant.Engines.Pdf.Tests;

internal static class MinimalPdfFactory
{
    public static byte[] Create(int widthPt = 595, int heightPt = 842)
    {
        // Use Latin1 encoding — PDF is a byte stream, not UTF-8.
        var enc = Encoding.Latin1;

        string header = "%PDF-1.4\n";
        string obj1 = "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n";
        string obj2 = "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n";
        string obj3 = $"3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 {widthPt} {heightPt}]>>\nendobj\n";

        int off1 = enc.GetByteCount(header);
        int off2 = off1 + enc.GetByteCount(obj1);
        int off3 = off2 + enc.GetByteCount(obj2);
        int xrefStart = off3 + enc.GetByteCount(obj3);

        // PDF spec requires exactly 20 bytes per xref entry including CRLF or SP+LF.
        // Format: "nnnnnnnnnn ggggg n \n"  (10 digit offset, space, 5 digit gen, space, 'n'/'f', space, LF)
        string xref =
            "xref\n" +
            "0 4\n" +
            "0000000000 65535 f \n" +
            $"{off1:D10} 00000 n \n" +
            $"{off2:D10} 00000 n \n" +
            $"{off3:D10} 00000 n \n";

        string trailer =
            $"trailer\n<</Size 4/Root 1 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n";

        string full = header + obj1 + obj2 + obj3 + xref + trailer;
        return enc.GetBytes(full);
    }
}
