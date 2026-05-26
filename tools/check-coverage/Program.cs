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
        IReadOnlySet<string>? enforce = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--report" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;
                case "--enforce" when i + 1 < args.Length:
                    enforce = ParseLayerList(args[++i]);
                    break;
                default:
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

        var blocking = violations
            .Where(v => enforce is not null && enforce.Contains(v.Assembly))
            .ToList();

        if (violations.Count > 0)
        {
            Console.Error.WriteLine(
                $"\n{violations.Count} ниже порога §6.2: "
                + string.Join("; ", violations.Select(v => v.Message)));
        }

        if (blocking.Count > 0)
        {
            Console.Error.WriteLine(
                $"{blocking.Count} enforced ниже порога: "
                + string.Join("; ", blocking.Select(v => v.Assembly)));
            return 1;
        }

        return 0;
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine(
            "usage: check-coverage --report <coverage.cobertura.xml | dir-with-reports> "
            + "[--enforce <layer,layer,...>]");

    internal static IReadOnlySet<string> ParseLayerList(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    internal static IEnumerable<string> ResolveReports(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.cobertura.xml", SearchOption.AllDirectories)
            : File.Exists(path) ? [path] : [];

    // Каждый assembly покрыт многими тест-проектами, каждый трогает свой поднабор строк.
    // Берём построчное объединение: для каждой (package, file, line) — MAX(hits) по всем
    // отчётам, затем line-rate = (строк с hits>0) / (всего строк). Это истинный union,
    // а не недооценка через MAX(per-report line-rate).
    internal static Dictionary<string, double> MergeCoverage(IEnumerable<string> coberturaXmls)
    {
        var unionHits = new Dictionary<string, Dictionary<(string File, int Line), int>>(StringComparer.Ordinal);
        var fallbackRate = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var xml in coberturaXmls)
        {
            foreach (var pkg in ParsePackages(xml))
            {
                if (pkg.Lines.Count == 0)
                {
                    // Отчёт без построчной детализации — оставляем package-level line-rate
                    // как запасной вариант (берём максимум, как раньше).
                    if (pkg.PackageRate is { } rate
                        && (!fallbackRate.TryGetValue(pkg.Name, out var prev) || rate > prev))
                    {
                        fallbackRate[pkg.Name] = rate;
                    }

                    continue;
                }

                if (!unionHits.TryGetValue(pkg.Name, out var lines))
                {
                    lines = new Dictionary<(string, int), int>();
                    unionHits[pkg.Name] = lines;
                }

                foreach (var (key, hits) in pkg.Lines)
                {
                    if (!lines.TryGetValue(key, out var existing) || hits > existing)
                    {
                        lines[key] = hits;
                    }
                }
            }
        }

        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, lines) in unionHits)
        {
            merged[name] = lines.Count == 0
                ? 0.0
                : (double)lines.Values.Count(h => h > 0) / lines.Count;
        }

        foreach (var (name, rate) in fallbackRate)
        {
            if (!merged.ContainsKey(name))
            {
                merged[name] = rate;
            }
        }

        return merged;
    }

    // Совместимость: package-level line-rate, как читался раньше.
    internal static IEnumerable<(string Name, double LineRate)> ParseCobertura(string xml)
    {
        foreach (var pkg in ParsePackages(xml))
        {
            if (pkg.PackageRate is { } rate)
            {
                yield return (pkg.Name, rate);
            }
        }
    }

    internal static IEnumerable<ParsedPackage> ParsePackages(string xml)
    {
        var doc = XDocument.Parse(xml);
        foreach (var pkg in doc.Descendants("package"))
        {
            var name = pkg.Attribute("name")?.Value;
            if (name is null)
            {
                continue;
            }

            double? packageRate = null;
            if (pkg.Attribute("line-rate")?.Value is { } rateAttr
                && double.TryParse(rateAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                packageRate = rate;
            }

            var lines = new Dictionary<(string File, int Line), int>();
            foreach (var cls in pkg.Descendants("class"))
            {
                var file = cls.Attribute("filename")?.Value ?? string.Empty;
                foreach (var line in cls.Descendants("line"))
                {
                    if (int.TryParse(
                            line.Attribute("number")?.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var number)
                        && int.TryParse(
                            line.Attribute("hits")?.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var hits))
                    {
                        var key = (file, number);
                        if (!lines.TryGetValue(key, out var existing) || hits > existing)
                        {
                            lines[key] = hits;
                        }
                    }
                }
            }

            yield return new ParsedPackage(name, packageRate, lines);
        }
    }

    internal static (List<string> Report, List<Violation> Violations) Evaluate(
        IReadOnlyDictionary<string, double> coverage,
        IReadOnlyDictionary<string, double> thresholds)
    {
        var report = new List<string>();
        var violations = new List<Violation>();
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
                violations.Add(new Violation(asm, $"{asm} {actual:P1} < {min:P0}"));
            }
        }

        return (report, violations);
    }

    internal readonly record struct ParsedPackage(
        string Name,
        double? PackageRate,
        IReadOnlyDictionary<(string File, int Line), int> Lines);

    internal readonly record struct Violation(string Assembly, string Message);
}
