using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Foliant.Tools.PerfCompare;

internal static class Program
{
    public static int Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null)
        {
            PrintUsage();
            return 2;
        }

        var baseline = LoadBaseline(opts.BaselinePath);
        var current = LoadCurrent(opts.CurrentPath);

        var (report, regressions) = Compare(baseline, current, opts.ThresholdPct);

        Console.WriteLine(string.Join('\n', report));

        if (regressions.Count > 0)
        {
            Console.Error.WriteLine($"\n{regressions.Count} регрессий выше {opts.ThresholdPct}%: {string.Join(", ", regressions)}");
            return 1;
        }

        return 0;
    }

    internal static (List<string> Report, List<string> Regressions) Compare(
        IReadOnlyDictionary<string, Bench> baseline,
        IReadOnlyDictionary<string, Bench> current,
        double thresholdPct)
    {
        var regressions = new List<string>();
        var report = new List<string>();

        foreach (var (name, b) in baseline)
        {
            if (!current.TryGetValue(name, out var c))
            {
                report.Add($"[skip] {name}: нет в текущем прогоне");
                continue;
            }
            var deltaPct = (c.P95 - b.P95) / b.P95 * 100;
            var marker = deltaPct > thresholdPct ? "REGRESS" : "ok";
            report.Add($"[{marker,-7}] {name}: p95 {b.P95:F1} → {c.P95:F1} ({deltaPct:+0.0;-0.0;0.0} %)");
            if (deltaPct > thresholdPct)
            {
                regressions.Add(name);
            }
        }

        return (report, regressions);
    }

    private static Options? ParseArgs(string[] args)
    {
        string? baseline = null, current = null;
        double threshold = 15;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline" when i + 1 < args.Length: baseline = args[++i]; break;
                case "--current" when i + 1 < args.Length: current = args[++i]; break;
                case "--threshold" when i + 1 < args.Length: threshold = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                default: return null;
            }
        }

        if (baseline is null || current is null)
        {
            return null;
        }
        return new Options(baseline, current, threshold);
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine("usage: perf-compare --baseline <baseline.json> --current <results-dir-or-file> [--threshold 15]");

    internal static Dictionary<string, Bench> LoadBaseline(string path)
    {
        using var stream = File.OpenRead(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, BenchRaw>>(stream)
            ?? throw new InvalidOperationException($"Empty baseline at {path}");
        return raw.ToDictionary(kv => kv.Key, kv => new Bench(kv.Value.P50_ms, kv.Value.P95_ms));
    }

    private static Dictionary<string, Bench> LoadCurrent(string path)
    {
        var files = ResolveReportFiles(path);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"[warn] Нет результатов в {path} — считаю прогон тривиально-зелёным.");
            return [];
        }

        var result = new Dictionary<string, Bench>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var (name, bench) in ParseReport(File.ReadAllText(file)))
            {
                // Last writer wins; multiple report files merge by method name.
                result[name] = bench;
            }
        }
        return result;
    }

    private static List<string> ResolveReportFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }
        if (!Directory.Exists(path))
        {
            return [];
        }
        // BDN's JsonExporter writes <Namespace.Type>-report-full.json; the dry/compressed
        // exporter writes -report-full-compressed.json. Both share the same Benchmarks shape.
        var full = Directory.GetFiles(path, "*-report-full.json", SearchOption.AllDirectories);
        if (full.Length > 0)
        {
            return [.. full];
        }
        return [.. Directory.GetFiles(path, "*-report-full-compressed.json", SearchOption.AllDirectories)];
    }

    /// <summary>
    /// Parses one BenchmarkDotNet <c>*-report-full.json</c> body into ms-keyed benches.
    /// Percentiles in the BDN report are nanoseconds; we convert ns→ms (÷1_000_000) and map the
    /// BDN <c>Method</c> name straight onto the baseline key.
    /// </summary>
    internal static Dictionary<string, Bench> ParseReport(string json)
    {
        var result = new Dictionary<string, Bench>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Benchmarks", out var benchmarks)
            || benchmarks.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in benchmarks.EnumerateArray())
        {
            if (!entry.TryGetProperty("Method", out var methodProp)
                || methodProp.GetString() is not { } method)
            {
                continue;
            }
            if (!entry.TryGetProperty("Statistics", out var stats)
                || stats.ValueKind != JsonValueKind.Object
                || !stats.TryGetProperty("Percentiles", out var pct)
                || pct.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var p50Ns = pct.TryGetProperty("P50", out var p50) ? p50.GetDouble() : 0d;
            var p95Ns = pct.TryGetProperty("P95", out var p95) ? p95.GetDouble() : 0d;

            result[method] = new Bench(NsToMs(p50Ns), NsToMs(p95Ns));
        }

        return result;
    }

    private static double NsToMs(double ns) => ns / 1_000_000d;

    internal sealed record Options(string BaselinePath, string CurrentPath, double ThresholdPct);
    internal sealed record Bench(double P50, double P95);

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Constructed by JsonSerializer.Deserialize via reflection.")]
    internal sealed record BenchRaw(double P50_ms, double P95_ms);
}
