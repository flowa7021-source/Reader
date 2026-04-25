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
            var marker = deltaPct > opts.ThresholdPct ? "REGRESS" : "ok";
            report.Add($"[{marker,-7}] {name}: p95 {b.P95:F1} → {c.P95:F1} ({deltaPct:+0.0;-0.0;0.0} %)");
            if (deltaPct > opts.ThresholdPct)
            {
                regressions.Add(name);
            }
        }

        Console.WriteLine(string.Join('\n', report));

        if (regressions.Count > 0)
        {
            Console.Error.WriteLine($"\n{regressions.Count} регрессий выше {opts.ThresholdPct}%: {string.Join(", ", regressions)}");
            return 1;
        }

        return 0;
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
                case "--current"  when i + 1 < args.Length: current  = args[++i]; break;
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
        Console.Error.WriteLine("usage: perf-compare --baseline <baseline.json> --current <results-dir> [--threshold 15]");

    private static Dictionary<string, Bench> LoadBaseline(string path)
    {
        using var stream = File.OpenRead(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, BenchRaw>>(stream)
            ?? throw new InvalidOperationException($"Empty baseline at {path}");
        return raw.ToDictionary(kv => kv.Key, kv => new Bench(kv.Value.P50_ms, kv.Value.P95_ms));
    }

    private static Dictionary<string, Bench> LoadCurrent(string dir)
    {
        // Phase 0 placeholder: BenchmarkDotNet ещё не пишет результаты.
        // Реальный парсинг результатов BDN будет добавлен вместе с tests/Foliant.Performance в S3.
        if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Length == 0)
        {
            Console.Error.WriteLine($"[warn] Нет результатов в {dir} — считаю прогон тривиально-зелёным.");
            return [];
        }

        // TODO: реализовать парсинг BDN JSON в S3.
        return [];
    }

    private sealed record Options(string BaselinePath, string CurrentPath, double ThresholdPct);
    private sealed record Bench(double P50, double P95);

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Constructed by JsonSerializer.Deserialize via reflection.")]
    private sealed record BenchRaw(double P50_ms, double P95_ms);
}
