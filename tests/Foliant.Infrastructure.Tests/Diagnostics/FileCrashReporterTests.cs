using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Infrastructure.Diagnostics;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Diagnostics;

public sealed class FileCrashReporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"foliant-crash-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static ISettingsService Settings(bool enabled)
    {
        var s = Substitute.For<ISettingsService>();
        s.Current.Returns(AppSettings.Default with { CrashReportingEnabled = enabled });
        return s;
    }

    [Fact]
    public void Report_WhenEnabled_WritesReportWithExceptionDetails()
    {
        var reporter = new FileCrashReporter(Settings(enabled: true), _dir);
        Exception captured;
        try
        {
            throw new InvalidOperationException("boom-42");
        }
        catch (InvalidOperationException e)
        {
            captured = e;
        }

        var path = reporter.Report(captured, "main-thread");

        path.Should().NotBeNull();
        File.Exists(path!).Should().BeTrue();
        var json = File.ReadAllText(path!);
        json.Should().Contain("InvalidOperationException");
        json.Should().Contain("boom-42");
        json.Should().Contain("main-thread");
    }

    [Fact]
    public void Report_WhenDisabled_ReturnsNullAndCreatesNothing()
    {
        var reporter = new FileCrashReporter(Settings(enabled: false), _dir);

        var path = reporter.Report(new InvalidOperationException("x"), "ctx");

        path.Should().BeNull();
        Directory.Exists(_dir).Should().BeFalse();
    }

    [Fact]
    public void Report_OnWriteFailure_ReturnsNullAndDoesNotThrow()
    {
        // Point the crash dir under an existing FILE so directory creation fails.
        var blocker = Path.Combine(Path.GetTempPath(), $"foliant-crash-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "x");
        try
        {
            var reporter = new FileCrashReporter(Settings(enabled: true), Path.Combine(blocker, "sub"));

            string? path = null;
            var act = () => path = reporter.Report(new InvalidOperationException("x"), "ctx");

            act.Should().NotThrow();
            path.Should().BeNull();
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
