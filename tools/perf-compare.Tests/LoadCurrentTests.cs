using FluentAssertions;
using Foliant.Tools.PerfCompare;
using Xunit;

namespace Foliant.Tools.PerfCompare.Tests;

public sealed class LoadCurrentTests
{
    private static string FixtureJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "Sample-report-full.json"));

    [Fact]
    public void ParseReport_ConvertsNsToMs_AndMapsMethodNamesToKeys()
    {
        var parsed = Program.ParseReport(FixtureJson());

        parsed.Should().ContainKey("SearchAcross10kPages");
        parsed.Should().ContainKey("ComputeFingerprint");

        // 1_650_000_000 ns ÷ 1e6 = 1650 ms; 660_000_000 ns = 660 ms.
        parsed["SearchAcross10kPages"].P95.Should().BeApproximately(1650.0, 1e-6);
        parsed["SearchAcross10kPages"].P50.Should().BeApproximately(660.0, 1e-6);
        parsed["ComputeFingerprint"].P95.Should().BeApproximately(2.0, 1e-6);
    }

    [Fact]
    public void ParseReport_NoBenchmarksArray_ReturnsEmpty()
    {
        Program.ParseReport("{}").Should().BeEmpty();
    }

    [Fact]
    public void Compare_WithinThreshold_NoRegression()
    {
        var baseline = new Dictionary<string, Program.Bench>(StringComparer.Ordinal)
        {
            ["SearchAcross10kPages"] = new(600, 1500),
        };
        var current = Program.ParseReport(FixtureJson());

        var (report, regressions) = Program.Compare(baseline, current, thresholdPct: 15);

        // 1650 vs 1500 = +10% < 15% → ok.
        regressions.Should().BeEmpty();
        report.Should().ContainSingle().Which.Should().Contain("ok");
    }

    [Fact]
    public void Compare_AboveThreshold_DetectsRegression()
    {
        var baseline = new Dictionary<string, Program.Bench>(StringComparer.Ordinal)
        {
            ["SearchAcross10kPages"] = new(600, 1500),
        };
        var current = Program.ParseReport(FixtureJson());

        var (_, regressions) = Program.Compare(baseline, current, thresholdPct: 5);

        // 1650 vs 1500 = +10% > 5% → regression.
        regressions.Should().ContainSingle().Which.Should().Be("SearchAcross10kPages");
    }

    [Fact]
    public void Compare_MissingFromCurrent_IsSkippedNotRegressed()
    {
        var baseline = new Dictionary<string, Program.Bench>(StringComparer.Ordinal)
        {
            ["OcrPageRus"] = new(1500, 3000),
        };
        var current = Program.ParseReport(FixtureJson());

        var (report, regressions) = Program.Compare(baseline, current, thresholdPct: 15);

        regressions.Should().BeEmpty();
        report.Should().ContainSingle().Which.Should().Contain("skip");
    }

    [Fact]
    public void Compare_MissingKeyAlongsideOkKey_SkipsMissingAndStaysRegressionFree()
    {
        var baseline = new Dictionary<string, Program.Bench>(StringComparer.Ordinal)
        {
            ["SearchAcross10kPages"] = new(600, 1500),
            ["OcrPageRus"] = new(1500, 3000),
        };
        var current = Program.ParseReport(FixtureJson());

        var (report, regressions) = Program.Compare(baseline, current, thresholdPct: 15);

        regressions.Should().BeEmpty();
        report.Should().Contain(line => line.Contains("[skip]") && line.Contains("OcrPageRus"));
        report.Should().Contain(line => line.Contains("ok") && line.Contains("SearchAcross10kPages"));
    }
}
