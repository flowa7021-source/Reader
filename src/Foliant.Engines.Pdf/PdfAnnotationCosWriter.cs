using System.Globalization;
using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Сериализатор Line/Arrow/Polygon аннотаций в текст PDF cos-объекта (<see cref="PdfPigAnnotationAppender"/>
/// дописывает их через инкрементальный апдейт). Coordinates — PDF user space (Y-up); /Rect
/// считается как bbox по всем точкам, цвет берётся из <see cref="Annotation.ColorHex"/>.
///
/// /LE [/None /OpenArrow] — стрелка только в конце (start = None, end = OpenArrow) — соответствует
/// UI: пользователь рисует стрелку «куда показывает», end = target. Subj ставим равным человеко-
/// читаемому имени типа («Line»/«Arrow»/«Polygon») чтобы reader (Acrobat) показал тип в палитре.
/// </summary>
internal static class PdfAnnotationCosWriter
{
    public static string WriteAnnotationObject(Annotation a, IndirectReference pageRef) => a.Kind switch
    {
        AnnotationKind.Line => WriteLine(a, pageRef, isArrow: false),
        AnnotationKind.Arrow => WriteLine(a, pageRef, isArrow: true),
        AnnotationKind.Polygon => WritePolygon(a, pageRef),
        _ => throw new ArgumentException($"Unsupported annotation kind {a.Kind} for cos writer.", nameof(a)),
    };

    private static string WriteLine(Annotation a, IndirectReference pageRef, bool isArrow)
    {
        var pts = a.InkPoints!;
        double x1 = pts[0].X, y1 = pts[0].Y, x2 = pts[1].X, y2 = pts[1].Y;
        (double minX, double minY, double maxX, double maxY) = (
            Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

        var sb = NewDict();
        AppendCommon(sb, "Line", pageRef, a, minX, minY, maxX, maxY);
        sb.Append(CultureInfo.InvariantCulture, $"/L [{F(x1)} {F(y1)} {F(x2)} {F(y2)}]\n");
        if (isArrow)
        {
            sb.Append("/LE [/None /OpenArrow]\n");
        }

        sb.Append(">>");
        return sb.ToString();
    }

    private static string WritePolygon(Annotation a, IndirectReference pageRef)
    {
        var pts = a.InkPoints!;
        double minX = pts[0].X, minY = pts[0].Y, maxX = pts[0].X, maxY = pts[0].Y;
        foreach (var p in pts)
        {
            if (p.X < minX) { minX = p.X; }
            if (p.X > maxX) { maxX = p.X; }
            if (p.Y < minY) { minY = p.Y; }
            if (p.Y > maxY) { maxY = p.Y; }
        }

        var sb = NewDict();
        AppendCommon(sb, "Polygon", pageRef, a, minX, minY, maxX, maxY);

        sb.Append("/Vertices [");
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(CultureInfo.InvariantCulture, $"{F(pts[i].X)} {F(pts[i].Y)}");
        }

        sb.Append("]\n");
        sb.Append(">>");
        return sb.ToString();
    }

    private static StringBuilder NewDict() => new("<<\n");

    private static void AppendCommon(
        StringBuilder sb, string subtype, IndirectReference pageRef, Annotation a,
        double xLL, double yLL, double xUR, double yUR)
    {
        sb.Append(CultureInfo.InvariantCulture, $"/Type /Annot\n/Subtype /{subtype}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"/Rect [{F(xLL)} {F(yLL)} {F(xUR)} {F(yUR)}]\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"/P {pageRef.ObjectNumber} {pageRef.Generation} R\n");

        if (HexColor.TryParse(a.ColorHex, out byte r, out byte g, out byte b))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"/C [{Color(r)} {Color(g)} {Color(b)}]\n");
        }

        if (!string.IsNullOrEmpty(a.Text))
        {
            AppendPdfString(sb, "/Contents", a.Text);
        }

        if (!string.IsNullOrEmpty(a.Author))
        {
            AppendPdfString(sb, "/T", a.Author);
        }

        if (!string.IsNullOrEmpty(a.Subject))
        {
            AppendPdfString(sb, "/Subj", a.Subject);
        }

        sb.Append(CultureInfo.InvariantCulture, $"/M (D:{a.CreatedAt.UtcDateTime:yyyyMMddHHmmss}Z)\n");
        if (a.ModifiedAt is { } modified)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"/CreationDate (D:{modified.UtcDateTime:yyyyMMddHHmmss}Z)\n");
        }
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Color(byte component) => (component / 255.0).ToString("0.######", CultureInfo.InvariantCulture);

    private static void AppendPdfString(StringBuilder sb, string key, string value)
    {
        // /Contents и подобные text-string поля в PDF используют либо PdfDocEncoding для ASCII,
        // либо UTF-16BE с BOM для non-ASCII (см. ISO 32000-1 §7.9.2). Для безопасности с любыми
        // строками (русский/emoji) пишем hex-string в UTF-16BE+BOM.
        sb.Append(key).Append(' ');
        if (IsAsciiSafe(value))
        {
            sb.Append('(').Append(EscapeLiteral(value)).Append(")\n");
            return;
        }

        sb.Append('<');
        byte[] utf16be = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(value)).ToArray();
        foreach (byte by in utf16be)
        {
            sb.Append(by.ToString("X2", CultureInfo.InvariantCulture));
        }

        sb.Append(">\n");
    }

    private static bool IsAsciiSafe(string value)
    {
        foreach (char c in value)
        {
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static string EscapeLiteral(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '(' or ')' or '\\':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
