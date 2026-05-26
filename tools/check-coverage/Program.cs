using System.Globalization;
using System.Xml.Linq;

namespace Foliant.Tools.CheckCoverage;

internal static class Program
{
    // §6.2 целевое покрытие: assembly → минимальная line-rate. Views не измеряем.
    internal static readonly IReadOnlyDictionary<string, double> DefaultThresholds =
        new Dictionary<string, double>
        {
            ["Foliant.Domain"] = 0.90,
            ["Foliant.Application"] = 0.80,
            ["Foliant.Infrastructure"] = 0.70,
            ["Foliant.ViewModels"] = 0.60,
        };

    public static int Main(string[] args)
    {
        string? reportPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--report" && i + 1 < args.Length)
            {
                reportPath = args[++i];
            }
            else
            {
                PrintUsage();
                return 2;
            }
        }

        if (reportPath is null)
        {
            PrintUsage();
            return 2;
        }

        var files = ResolveReports(reportPath).ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"Нет *.cobertura.xml в {reportPath}");
            return 2;
        }

        var coverage = MergeCoverage(files.Select(File.ReadAllText));
        var (report, violations) = Evaluate(coverage, DefaultThresholds);
        Console.WriteLine(string.Join('\n', report));

        if (violations.Count > 0)
        {
            Console.Error.WriteLine($"\n{violations.Count} ниже порога §6.2: {string.Join("; ", violations)}");
            return 1;
        }

        return 0;
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("usage: check-coverage --report <coverage.cobertura.xml | dir-with-reports>");

    internal static IEnumerable<string> ResolveReports(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.cobertura.xml", SearchOption.AllDirectories)
            : File.Exists(path) ? [path] : [];

    // Покрытие одного assembly меряется только теми тест-проектами, что его трогают, поэтому
    // берём лучшую (макс) line-rate среди всех прогонов — приближение объединённого покрытия.
    internal static Dictionary<string, double> MergeCoverage(IEnumerable<string> coberturaXmls)
    {
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var xml in coberturaXmls)
        {
            foreach (var (name, rate) in ParseCobertura(xml))
            {
                if (!merged.TryGetValue(name, out var existing) || rate > existing)
                {
                    merged[name] = rate;
                }
            }
        }

        return merged;
    }

    internal static IEnumerable<(string Name, double LineRate)> ParseCobertura(string xml)
    {
        var doc = XDocument.Parse(xml);
        foreach (var pkg in doc.Descendants("package"))
        {
            var name = pkg.Attribute("name")?.Value;
            var rateAttr = pkg.Attribute("line-rate")?.Value;
            if (name is not null && rateAttr is not null
                && double.TryParse(rateAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                yield return (name, rate);
            }
        }
    }

    internal static (List<string> Report, List<string> Violations) Evaluate(
        IReadOnlyDictionary<string, double> coverage,
        IReadOnlyDictionary<string, double> thresholds)
    {
        var report = new List<string>();
        var violations = new List<string>();
        foreach (var (asm, min) in thresholds.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!coverage.TryGetValue(asm, out var actual))
            {
                report.Add($"[skip] {asm}: нет в отчёте");
                continue;
            }

            var ok = actual >= min;
            report.Add($"[{(ok ? "ok" : "FAIL"),-4}] {asm}: {actual:P1} (порог {min:P0})");
            if (!ok)
            {
                violations.Add($"{asm} {actual:P1} < {min:P0}");
            }
        }

        return (report, violations);
    }
}
