using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentMetadataViewModelTests
{
    [Fact]
    public void EmptyMetadata_AllFieldsShowPlaceholder()
    {
        var vm = new DocumentMetadataViewModel(DocumentMetadata.Empty, "/x.pdf", pageCount: 0);

        vm.Title.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.Author.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.Subject.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.Created.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.Modified.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.HasAnyKnownField.Should().BeFalse();
    }

    [Fact]
    public void TitleAuthorSubject_AreShownVerbatimWhenPresent()
    {
        var meta = new DocumentMetadata(
            Title: "Война и мир",
            Author: "Лев Толстой",
            Subject: "Историческая проза",
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());
        var vm = new DocumentMetadataViewModel(meta, "/wp.pdf", 1300);

        vm.Title.Should().Be("Война и мир");
        vm.Author.Should().Be("Лев Толстой");
        vm.Subject.Should().Be("Историческая проза");
        vm.HasAnyKnownField.Should().BeTrue();
    }

    [Fact]
    public void Dates_FormattedAsInvariantIsoLike()
    {
        var when = new DateTimeOffset(2026, 4, 30, 12, 34, 56, TimeSpan.FromHours(3));
        var meta = new DocumentMetadata(null, null, null, when, when.AddDays(1), new Dictionary<string, string>());
        var vm = new DocumentMetadataViewModel(meta, null, 0);

        vm.Created.Should().Be("2026-04-30 12:34:56+03:00");
        vm.Modified.Should().Be("2026-05-01 12:34:56+03:00");
    }

    [Fact]
    public void CustomEntries_PopulatedFromMetadata()
    {
        var meta = new DocumentMetadata(
            null, null, null, null, null,
            Custom: new Dictionary<string, string>
            {
                ["Producer"] = "PDFium",
                ["Keywords"] = "test;pdf",
            });
        var vm = new DocumentMetadataViewModel(meta, "/x.pdf", 1);

        vm.Custom.Should().HaveCount(2);
        vm.Custom.Should().Contain(e => e.Key == "Producer" && e.Value == "PDFium");
    }

    [Fact]
    public void FilePath_NullProvided_FallsBackToPlaceholder()
    {
        var vm = new DocumentMetadataViewModel(DocumentMetadata.Empty, filePath: null, pageCount: 5);

        vm.FilePath.Should().Be(DocumentMetadataViewModel.MissingPlaceholder);
        vm.PageCount.Should().Be(5);
    }

    [Fact]
    public void HasAnyKnownField_TrueWhenOnlyDatePresent()
    {
        var meta = new DocumentMetadata(null, null, null, DateTimeOffset.UtcNow, null, new Dictionary<string, string>());
        var vm = new DocumentMetadataViewModel(meta, "/x.pdf", 1);

        vm.HasAnyKnownField.Should().BeTrue();
    }
}
