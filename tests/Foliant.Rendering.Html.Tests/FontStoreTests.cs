using FluentAssertions;
using SixLabors.Fonts;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

public sealed class FontStoreTests
{
    public static TheoryData<GenericFontFamily, bool, bool> AllFaces()
    {
        var data = new TheoryData<GenericFontFamily, bool, bool>();
        foreach (GenericFontFamily family in Enum.GetValues<GenericFontFamily>())
        {
            foreach (bool bold in new[] { false, true })
            {
                foreach (bool italic in new[] { false, true })
                {
                    data.Add(family, bold, italic);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFaces))]
    public void Resolve_AllGenericFamilyAndStyleCombinations_ReturnNonNullFont(
        GenericFontFamily family, bool bold, bool italic)
    {
        var store = new FontStore();

        Font font = store.Resolve(family, bold, italic, 16f);

        font.Should().NotBeNull();
        font.Size.Should().Be(16f);
    }

    [Fact]
    public void Resolve_ClampsNonPositiveSizeToPositive()
    {
        var store = new FontStore();

        Font font = store.Resolve(GenericFontFamily.Serif, bold: false, italic: false, sizePx: 0f);

        font.Size.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Resolve_DistinctFamilies_MapToDistinctFontFamilies()
    {
        var store = new FontStore();

        Font serif = store.Resolve(GenericFontFamily.Serif, false, false, 20f);
        Font sans = store.Resolve(GenericFontFamily.SansSerif, false, false, 20f);
        Font mono = store.Resolve(GenericFontFamily.Monospace, false, false, 20f);

        serif.Family.Name.Should().NotBe(sans.Family.Name);
        sans.Family.Name.Should().NotBe(mono.Family.Name);
        serif.Family.Name.Should().NotBe(mono.Family.Name);
    }

    [Fact]
    public void Resolve_HonoursRequestedSize()
    {
        var store = new FontStore();

        Font font = store.Resolve(GenericFontFamily.SansSerif, bold: true, italic: true, sizePx: 33.5f);

        font.Size.Should().Be(33.5f);
    }
}
