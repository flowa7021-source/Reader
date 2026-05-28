using FluentAssertions;
using Foliant.Engines.Pdf;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Чистые unit-тесты для placeholder-расширения — нативный PDFium не нужен, отдельно от
/// Slow-категории.
/// </summary>
public sealed class PdfiumHeaderFooterPlaceholdersTests
{
    [Fact]
    public void Expands_PageAndTotal_OneBased()
    {
        string result = PdfiumHeaderFooterService.ExpandPlaceholders(
            "Page {page} of {total}", pageIndex: 4, totalPages: 10, filename: "x.pdf", today: "2025-01-15");

        result.Should().Be("Page 5 of 10");
    }

    [Fact]
    public void Expands_Filename_AndDate()
    {
        string result = PdfiumHeaderFooterService.ExpandPlaceholders(
            "{filename} — {date}", pageIndex: 0, totalPages: 1, filename: "Doc 1.pdf", today: "2025-01-15");

        result.Should().Be("Doc 1.pdf — 2025-01-15");
    }

    [Fact]
    public void TemplateWithoutPlaceholders_PassesThroughVerbatim()
    {
        string result = PdfiumHeaderFooterService.ExpandPlaceholders(
            "Confidential", pageIndex: 0, totalPages: 1, filename: "x.pdf", today: "2025-01-15");

        result.Should().Be("Confidential");
    }

    [Fact]
    public void UnknownPlaceholder_IsNotExpanded()
    {
        // Не маскируем неизвестный токен — пользователь увидит литерал и поймёт что опечатался.
        string result = PdfiumHeaderFooterService.ExpandPlaceholders(
            "{author} — {date}", pageIndex: 0, totalPages: 1, filename: "x.pdf", today: "2025-01-15");

        result.Should().Be("{author} — 2025-01-15");
    }

    [Fact]
    public void MultipleOccurrences_AllReplaced()
    {
        string result = PdfiumHeaderFooterService.ExpandPlaceholders(
            "{page}/{total} — {page} of {total}", pageIndex: 2, totalPages: 9, filename: "x.pdf", today: "2025-01-15");

        result.Should().Be("3/9 — 3 of 9");
    }
}
