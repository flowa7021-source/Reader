using FluentAssertions;
using Foliant.Tools.CheckCoverage;
using Xunit;

namespace Foliant.Tools.CheckCoverage.Tests;

public sealed class CoverageGateTests
{
    private const string Report =
        """
        <?xml version="1.0"?>
        <coverage line-rate="0.8">
          <packages>
            <package name="Foliant.Domain" line-rate="0.95" />
            <package name="Foliant.Application" line-rate="0.82" />
            <package name="Foliant.Infrastructure" line-rate="0.65" />
            <package name="Foliant.ViewModels" line-rate="0.61" />
          </packages>
        </coverage>
        """;

    [Fact]
    public void ParseCobertura_ReadsPackageLineRates()
    {
        var parsed = Program.ParseCobertura(Report).ToDictionary(p => p.Name, p => p.LineRate);

        parsed.Should().HaveCount(4);
        parsed["Foliant.Domain"].Should().BeApproximately(0.95, 1e-9);
        parsed["Foliant.Infrastructure"].Should().BeApproximately(0.65, 1e-9);
    }

    [Fact]
    public void MergeCoverage_TakesMaxAcrossReports()
    {
        const string second =
            """
            <?xml version="1.0"?>
            <coverage><packages>
              <package name="Foliant.Infrastructure" line-rate="0.72" />
            </packages></coverage>
            """;

        var merged = Program.MergeCoverage([Report, second]);

        merged["Foliant.Infrastructure"].Should().BeApproximately(0.72, 1e-9);
    }

    [Fact]
    public void Evaluate_FlagsAssemblyBelowThreshold()
    {
        var coverage = Program.MergeCoverage([Report]);

        var (_, violations) = Program.Evaluate(coverage, Program.DefaultThresholds);

        violations.Should().ContainSingle().Which.Should().Contain("Foliant.Infrastructure");
    }

    [Fact]
    public void Evaluate_AllAboveThreshold_NoViolations()
    {
        var coverage = new Dictionary<string, double>
        {
            ["Foliant.Domain"] = 0.91,
            ["Foliant.Application"] = 0.85,
            ["Foliant.Infrastructure"] = 0.71,
            ["Foliant.ViewModels"] = 0.60,
        };

        var (_, violations) = Program.Evaluate(coverage, Program.DefaultThresholds);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MissingAssembly_IsSkippedNotFailed()
    {
        var coverage = new Dictionary<string, double> { ["Foliant.Domain"] = 0.95 };

        var (report, violations) = Program.Evaluate(coverage, Program.DefaultThresholds);

        violations.Should().BeEmpty();
        report.Should().Contain(line => line.Contains("Foliant.Application") && line.Contains("skip"));
    }
}
