using BenchmarkDotNet.Attributes;
using Foliant.Infrastructure.Storage;

namespace Foliant.Performance;

[MemoryDiagnoser]
[BenchmarkCategory("CrossPlatform")]
public class FileFingerprintBenchmarks
{
    private readonly FileFingerprint _sut = new();
    private string _path = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 1 MiB exercises the full 64KiB head window plus the size/mtime tail.
        var bytes = new byte[1024 * 1024];
        new Random(1234).NextBytes(bytes);
        _path = Path.Combine(Path.GetTempPath(), "foliant-bench-fp-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(_path, bytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // best-effort cleanup of the temp sample
        }
    }

    [Benchmark]
    public async Task<string> ComputeFingerprint() =>
        await _sut.ComputeAsync(_path, CancellationToken.None);
}
