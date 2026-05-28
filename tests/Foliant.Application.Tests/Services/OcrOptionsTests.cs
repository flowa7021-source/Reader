using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class OcrOptionsTests
{
    [Fact]
    public void Default_Languages_AndZeroMinConfidence_AreBackwardCompatible()
    {
        var opts = new OcrOptions();

        opts.Languages.Should().Be("eng+rus");
        opts.MinConfidence.Should().Be(0.0);
    }

    [Fact]
    public void Explicit_MinConfidence_Roundtrips()
    {
        var opts = new OcrOptions(MinConfidence: 0.6);

        opts.MinConfidence.Should().Be(0.6);
    }
}
