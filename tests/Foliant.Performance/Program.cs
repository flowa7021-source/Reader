using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Foliant.Performance;

internal static class Program
{
    public static int Main(string[] args)
    {
        // --quick is a CI smoke shortcut equivalent to BDN's `--job short`; when neither is
        // present we fall back to the default (statistically heavier) job.
        var quick = args.Contains("--quick");

        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddColumn(CategoriesColumn.Default)
            .WithOption(ConfigOptions.JoinSummary, true);

        if (quick)
        {
            config.AddJob(Job.ShortRun.WithIterationCount(3).WithWarmupCount(1));
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}
