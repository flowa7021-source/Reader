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

    [Fact]
    public void For_EncodesRotation_IntoBits5And6()
    {
        var none = CacheKey.For("fp", 0, 1, RenderOptions.Default with { RenderAnnotations = false });
        var cw90 = CacheKey.For("fp", 0, 1, RenderOptions.Default with { RenderAnnotations = false, Rotation = ViewRotation.Cw90 });
        var cw180 = CacheKey.For("fp", 0, 1, RenderOptions.Default with { RenderAnnotations = false, Rotation = ViewRotation.Cw180 });
        var cw270 = CacheKey.For("fp", 0, 1, RenderOptions.Default with { RenderAnnotations = false, Rotation = ViewRotation.Cw270 });

        none.Flags.Should().Be(0);
        cw90.Flags.Should().Be(1 << 5);
        cw180.Flags.Should().Be(2 << 5);
        cw270.Flags.Should().Be(3 << 5);
    }

    [Fact]
    public void For_RotationCombinedWithThemeAndAnnotations_BitsDoNotCollide()
    {
        var opts = RenderOptions.Default with
        {
            RenderAnnotations = true,
            Theme = RenderTheme.HighContrast,
            Rotation = ViewRotation.Cw270,
        };

        var key = CacheKey.For("fp", 0, 1, opts);

        // bit0 = 1 (annotations), bits1-4 = HighContrast (2) → 2<<1 = 4, bits5-6 = 3<<5 = 96
        key.Flags.Should().Be(1 | 4 | 96);
    }
}
