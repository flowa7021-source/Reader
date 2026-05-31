using System.Text;

namespace Foliant.Engines.Fb2.Tests;

/// <summary>
/// Hand-builds a minimal valid FB2 XML for unit tests. Single body, configurable
/// title-info + sections.
/// </summary>
internal static class Fb2TestFactory
{
    public static string Create(string targetDir, string bookTitle, string firstName, string lastName, params string[] sectionParagraphs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        Directory.CreateDirectory(targetDir);
        string path = Path.Combine(targetDir, $"book-{Guid.NewGuid():N}.fb2");

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<FictionBook xmlns=\"http://www.gribuser.ru/xml/fictionbook/2.0\">");
        sb.AppendLine("  <description>");
        sb.AppendLine("    <title-info>");
        sb.Append("      <book-title>").Append(System.Net.WebUtility.HtmlEncode(bookTitle)).AppendLine("</book-title>");
        sb.AppendLine("      <author>");
        sb.Append("        <first-name>").Append(System.Net.WebUtility.HtmlEncode(firstName)).AppendLine("</first-name>");
        sb.Append("        <last-name>").Append(System.Net.WebUtility.HtmlEncode(lastName)).AppendLine("</last-name>");
        sb.AppendLine("      </author>");
        sb.AppendLine("      <lang>en</lang>");
        sb.AppendLine("    </title-info>");
        sb.AppendLine("  </description>");
        sb.AppendLine("  <body>");
        for (int i = 0; i < sectionParagraphs.Length; i++)
        {
            sb.AppendLine("    <section>");
            sb.Append("      <title><p>Section ").Append(i + 1).AppendLine("</p></title>");
            sb.Append("      <p>").Append(System.Net.WebUtility.HtmlEncode(sectionParagraphs[i])).AppendLine("</p>");
            sb.AppendLine("    </section>");
        }
        sb.AppendLine("  </body>");
        sb.AppendLine("</FictionBook>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>FB2 без секций — только <c>&lt;body&gt;&lt;p&gt;...</c>. Используется для тестирования
    /// fallback-пути <c>Fb2Document.CollectPagesFromBody</c>.</summary>
    public static string CreateBodyOnly(string targetDir, string title, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        Directory.CreateDirectory(targetDir);
        string path = Path.Combine(targetDir, $"body-only-{Guid.NewGuid():N}.fb2");

        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
              <description><title-info><book-title>{System.Net.WebUtility.HtmlEncode(title)}</book-title><lang>en</lang></title-info></description>
              <body><p>{System.Net.WebUtility.HtmlEncode(content)}</p></body>
            </FictionBook>
            """;
        File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
