using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Updates;

public sealed class GitHubUpdateCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewerTag_SetsUpdateAvailable()
    {
        var (sut, _, settings) = CreateSut("v999.0.0");

        UpdateCheckResult result = await sut.CheckAsync(CancellationToken.None);

        result.UpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be(new Version(999, 0, 0));
        settings.Current.LastUpdateCheckTime.Should().Be(Now);
    }

    [Fact]
    public async Task SameVersion_NoUpdate()
    {
        // Resolve the service's own notion of "current" first, then feed it back as the latest tag.
        var (probe, _, _) = CreateSut(latestTag: null);
        Version current = (await probe.CheckAsync(CancellationToken.None)).CurrentVersion;

        var (sut, _, _) = CreateSut(current.ToString());

        UpdateCheckResult result = await sut.CheckAsync(CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task OlderTag_NoUpdate()
    {
        var (sut, _, _) = CreateSut("v0.0.1");

        UpdateCheckResult result = await sut.CheckAsync(CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task SecondCallWithin24h_DoesNotReQuery()
    {
        var clock = new MutableClock(Now);
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestTagAsync(Arg.Any<CancellationToken>()).Returns("v999.0.0");
        var settings = new FakeSettingsService();
        var sut = new GitHubUpdateCheckService(source, settings, clock, NullLogger<GitHubUpdateCheckService>.Instance);

        await sut.CheckAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(23));
        await sut.CheckAsync(CancellationToken.None);

        await source.Received(1).GetLatestTagAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondCallAfter24h_ReQueries()
    {
        var clock = new MutableClock(Now);
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestTagAsync(Arg.Any<CancellationToken>()).Returns("v999.0.0");
        var settings = new FakeSettingsService();
        var sut = new GitHubUpdateCheckService(source, settings, clock, NullLogger<GitHubUpdateCheckService>.Instance);

        await sut.CheckAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(25));
        await sut.CheckAsync(CancellationToken.None);

        await source.Received(2).GetLatestTagAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptOut_DoesNotQuery()
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestTagAsync(Arg.Any<CancellationToken>()).Returns("v999.0.0");
        var settings = new FakeSettingsService(AppSettings.Default with { CheckForUpdates = false });
        var sut = new GitHubUpdateCheckService(source, settings, new MutableClock(Now), NullLogger<GitHubUpdateCheckService>.Instance);

        UpdateCheckResult result = await sut.CheckAsync(CancellationToken.None);

        result.UpdateAvailable.Should().BeFalse();
        await source.DidNotReceive().GetLatestTagAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNoUpdate_DoesNotThrow()
    {
        // Release source swallows network errors → null; service must treat that as "no update".
        var (sut, _, _) = CreateSut(latestTag: null);

        var act = async () => await sut.CheckAsync(CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.UpdateAvailable.Should().BeFalse();
        result.Subject.LatestVersion.Should().BeNull();
    }

    private static (GitHubUpdateCheckService Sut, IReleaseSource Source, FakeSettingsService Settings) CreateSut(string? latestTag)
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestTagAsync(Arg.Any<CancellationToken>()).Returns(latestTag);
        var settings = new FakeSettingsService();
        var sut = new GitHubUpdateCheckService(source, settings, new MutableClock(Now), NullLogger<GitHubUpdateCheckService>.Instance);
        return (sut, source, settings);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public FakeSettingsService(AppSettings? initial = null) => Current = initial ?? AppSettings.Default;

        public AppSettings Current { get; private set; }

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings, CancellationToken ct)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct)
        {
            Current = mutate(Current);
            return Task.CompletedTask;
        }
    }
}
