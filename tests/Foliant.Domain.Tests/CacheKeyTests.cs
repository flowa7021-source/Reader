using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class CacheKeyTests
{
    [Fact]
    public void ToFileName_IsStable_ForSameInputs()
    {
        var key = new CacheKey("abc123", PageIndex: 5, EngineVersion: 2, ZoomBucket: 100, Flags: 1);

        key.ToFileName().Should().Be("abc123_5_2_100_1.bin");
    }

    [Fact]
    public void For_EncodesAnnotationsAndThemeIntoFlags()
    {
        var dark = RenderOptions.Default with { Theme = RenderTheme.Dark, RenderAnnotations = true };

        var key = CacheKey.For("fp", pageIndex: 0, engineVersion: 1, dark);

        // bit0 = annotations on; theme.Dark = 1 → bits1+ = 1<<1 = 2 → итого 3
        key.Flags.Should().Be(3);
    }

    [Fact]
    public void For_HighContrastNoAnnotations_EncodesCorrectly()
    {
        var hc = RenderOptions.Default with { Theme = RenderTheme.HighContrast, RenderAnnotations = false };

        var key = CacheKey.For("fp", pageIndex: 0, engineVersion: 1, hc);

        // bit0 = 0; theme.HighContrast = 2 → 2<<1 = 4
        key.Flags.Should().Be(4);
    }

    [Fact]
    public void TwoKeysWithSameInputs_AreEqual()
    {
        var a = new CacheKey("fp", 1, 1, 100, 0);
        var b = new CacheKey("fp", 1, 1, 100, 0);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ChangingAnyField_ChangesFileName()
    {
        var baseKey = new CacheKey("fp", 1, 1, 100, 0);

        baseKey.ToFileName().Should().NotBe((baseKey with { PageIndex = 2 }).ToFileName());
        baseKey.ToFileName().Should().NotBe((baseKey with { EngineVersion = 2 }).ToFileName());
        baseKey.ToFileName().Should().NotBe((baseKey with { ZoomBucket = 125 }).ToFileName());
        baseKey.ToFileName().Should().NotBe((baseKey with { Flags = 1 }).ToFileName());
        baseKey.ToFileName().Should().NotBe((baseKey with { DocFingerprint = "fp2" }).ToFileName());
    }
}
