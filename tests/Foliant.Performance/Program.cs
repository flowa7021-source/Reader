using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Foliant.Performance;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Quick mode для CI smoke (см. ci.yml).
        var quick = args.Contains("--quick");

        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(quick
                ? Job.ShortRun.WithIterationCount(3).WithWarmupCount(1)
                : Job.Default);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}
