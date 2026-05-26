using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Foliant.Performance.Native;

internal static class Program
{
    public static int Main(string[] args)
    {
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
