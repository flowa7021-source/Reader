using FluentAssertions;
using Foliant.Engines.Ocr;
using Xunit;

namespace Foliant.Engines.Ocr.Tests;

public sealed class OcrLanguageMapTests
{
    [Theory]
    [InlineData("eng")]
    [InlineData("deu+fra")]
    [InlineData("spa")]
    [InlineData("")]
    public void Resolve_LatinOnly_ReturnsLatin(string languages)
    {
        OcrLanguageMap.Resolve(languages).Should().Be(OcrModelKind.Latin);
    }

    [Theory]
    [InlineData("rus")]
    [InlineData("eng+rus")]
    [InlineData("ukr+eng")]
    [InlineData("kaz")]
    public void Resolve_AnyCyrillic_ReturnsCyrillic(string languages)
    {
        OcrLanguageMap.Resolve(languages).Should().Be(OcrModelKind.Cyrillic);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        OcrLanguageMap.Resolve("RUS").Should().Be(OcrModelKind.Cyrillic);
        OcrLanguageMap.Resolve("ENG").Should().Be(OcrModelKind.Latin);
    }

    [Fact]
    public void Resolve_Null_Throws()
    {
        Action act = () => OcrLanguageMap.Resolve(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
