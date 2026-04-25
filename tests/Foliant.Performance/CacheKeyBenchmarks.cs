using BenchmarkDotNet.Attributes;
using Foliant.Domain;

namespace Foliant.Performance;

[MemoryDiagnoser]
public class CacheKeyBenchmarks
{
    private readonly RenderOptions _opts = RenderOptions.Default with
    {
        Theme = RenderTheme.Dark,
        RenderAnnotations = true,
    };

    [Benchmark]
    public CacheKey Construct() =>
        CacheKey.For("0123456789abcdef0123456789abcdef", pageIndex: 42, engineVersion: 7, _opts);

    [Benchmark]
    public string ToFileName() =>
        new CacheKey("0123456789abcdef", 42, 7, 100, 3).ToFileName();
}
