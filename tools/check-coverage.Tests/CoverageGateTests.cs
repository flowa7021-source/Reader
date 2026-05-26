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

    // Два отчёта покрывают РАЗНЫЕ строки одного и того же package: первый — строки 1,2 (1 из 4),
    // второй — строки 3,4. Union = 4 из 4 строк → line-rate выше любого отдельного отчёта.
    private const string LinesReportA =
        """
        <?xml version="1.0"?>
        <coverage><packages>
          <package name="Foliant.Infrastructure" line-rate="0.25">
            <classes>
              <class filename="Cache.cs">
                <lines>
                  <line number="1" hits="3" />
                  <line number="2" hits="0" />
                  <line number="3" hits="0" />
                  <line number="4" hits="0" />
                </lines>
              </class>
            </classes>
          </package>
        </packages></coverage>
        """;

    private const string LinesReportB =
        """
        <?xml version="1.0"?>
        <coverage><packages>
          <package name="Foliant.Infrastructure" line-rate="0.25">
            <classes>
              <class filename="Cache.cs">
                <lines>
                  <line number="1" hits="0" />
                  <line number="2" hits="0" />
                  <line number="3" hits="1" />
                  <line number="4" hits="7" />
                </lines>
              </class>
            </classes>
          </package>
        </packages></coverage>
        """;

    [Fact]
    public void MergeCoverage_FallsBackToMaxWhenNoLineDetail()
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
    public void MergeCoverage_UnionsLinesAcrossReports_HigherThanEither()
    {
        var onlyA = Program.MergeCoverage([LinesReportA]);
        var onlyB = Program.MergeCoverage([LinesReportB]);
        var union = Program.MergeCoverage([LinesReportA, LinesReportB]);

        onlyA["Foliant.Infrastructure"].Should().BeApproximately(0.25, 1e-9);
        onlyB["Foliant.Infrastructure"].Should().BeApproximately(0.50, 1e-9);

        // 1 из A + 3,4 из B = 3 из 4 строк → union строго больше каждого отдельного.
        union["Foliant.Infrastructure"].Should().BeApproximately(0.75, 1e-9);
        union["Foliant.Infrastructure"].Should().BeGreaterThan(onlyA["Foliant.Infrastructure"]);
        union["Foliant.Infrastructure"].Should().BeGreaterThan(onlyB["Foliant.Infrastructure"]);
    }

    [Fact]
    public void Evaluate_FlagsAssemblyBelowThreshold()
    {
        var coverage = Program.MergeCoverage([Report]);

        var (_, violations) = Program.Evaluate(coverage, Program.DefaultThresholds);

        violations.Should().ContainSingle().Which.Assembly.Should().Be("Foliant.Infrastructure");
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

    [Fact]
    public void ParseLayerList_SplitsAndTrims()
    {
        var layers = Program.ParseLayerList(" Foliant.Domain , Foliant.Application ,");

        layers.Should().BeEquivalentTo(["Foliant.Domain", "Foliant.Application"]);
    }

    [Fact]
    public void Main_NoEnforce_BelowThreshold_ReportsButExitsZero()
    {
        using var fixture = new ReportFixture(Report);

        var exit = Program.Main(["--report", fixture.Path]);

        exit.Should().Be(0);
    }

    [Fact]
    public void Main_EnforcedLayerBelowThreshold_ExitsOne()
    {
        using var fixture = new ReportFixture(Report);

        var exit = Program.Main(["--report", fixture.Path, "--enforce", "Foliant.Infrastructure"]);

        exit.Should().Be(1);
    }

    [Fact]
    public void Main_NonEnforcedLayerBelowThreshold_ExitsZero()
    {
        // Infrastructure (0.65) ниже порога, но enforce'им только Domain (который выше порога).
        using var fixture = new ReportFixture(Report);

        var exit = Program.Main(["--report", fixture.Path, "--enforce", "Foliant.Domain"]);

        exit.Should().Be(0);
    }

    private sealed class ReportFixture : IDisposable
    {
        public ReportFixture(string xml)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"check-coverage-{Guid.NewGuid():N}.cobertura.xml");
            File.WriteAllText(Path, xml);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
