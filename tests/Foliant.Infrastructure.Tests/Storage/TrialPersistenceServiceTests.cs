using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Xunit;

namespace Foliant.Infrastructure.Tests.Storage;

/// <summary>
/// DPAPI + HKCU зависят от Windows-runtime → Slow + non-Windows no-op. Каждый
/// instance держит изолированный TempDir и registry-subkey, удаляемый в Dispose.
/// Tamper-сценарии делегируют чистой <c>TrialAntiTamperService</c> (отдельно
/// покрыта unit-тестами) — здесь проверяем именно тройную персистентность.
/// </summary>
[Trait("Category", "Slow")]
public sealed class TrialPersistenceServiceTests : IDisposable
{
    private const string TestRoot = @"Software\Foliant-Tests";
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDir _tmp = new();
    private readonly string _subKey = TestRoot + "\\" + Guid.NewGuid().ToString("N");
    private readonly MutableClock _clock = new(BaseTime);

    private TrialPersistenceService CreateSut()
    {
        var stores = new TrialStores(
            _tmp.File("trial.dat"),
            _tmp.File(".trial-marker"),
            _subKey,
            "Trial",
            NullLogger<TrialStores>.Instance);
        return new TrialPersistenceService(stores, _clock, NullLogger<TrialPersistenceService>.Instance);
    }

    [Fact]
    public async Task Evaluate_CleanSystem_ReturnsNotStarted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await CreateSut().EvaluateAsync(default);

        result.Status.Should().Be(TrialStatus.NotStarted);
        result.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays);
    }

    [Fact]
    public async Task Start_FreshSystem_ReturnsActive_FullDays_AndPersistsAllThree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await CreateSut().StartAsync(default);

        result.Status.Should().Be(TrialStatus.Active);
        result.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays);
        File.Exists(_tmp.File("trial.dat")).Should().BeTrue();
        File.Exists(_tmp.File(".trial-marker")).Should().BeTrue();
        ReadRegistryValue().Should().NotBeNull();
    }

    [Fact]
    public async Task Start_WhenAlreadyStarted_DoesNotResetStartDate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        _clock.Advance(TimeSpan.FromDays(10));
        var second = await sut.StartAsync(default);

        // Повторный Start не перезаписывает StartedAt → отсчёт продолжается, не сбрасывается.
        second.Status.Should().Be(TrialStatus.Active);
        second.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays - 10);
    }

    [Fact]
    public async Task Evaluate_AfterStartAndTimePasses_CountsDown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        _clock.Advance(TimeSpan.FromDays(12));
        var result = await sut.EvaluateAsync(default);

        result.Status.Should().Be(TrialStatus.Active);
        result.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays - 12);
    }

    [Fact]
    public async Task Evaluate_PastTrialPeriod_ReturnsExpired()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        _clock.Advance(TimeSpan.FromDays(TrialAntiTamperService.TrialDays + 1));
        var result = await sut.EvaluateAsync(default);

        result.Status.Should().Be(TrialStatus.Expired);
        result.DaysRemaining.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_MarkerFileDeleted_ReturnsTampered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        File.Delete(_tmp.File(".trial-marker")); // one store gone while others remain.

        var result = await sut.EvaluateAsync(default);
        result.Status.Should().Be(TrialStatus.Tampered);
        result.TamperReason.Should().Contain("missing");
    }

    [Fact]
    public async Task Evaluate_RegistryDeleted_ReturnsTampered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        DeleteRegistryValue(); // secondary store gone.

        var result = await sut.EvaluateAsync(default);
        result.Status.Should().Be(TrialStatus.Tampered);
    }

    [Fact]
    public async Task Touch_AdvancesMaxObserved_SoLaterClockRollbackIsTampered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);

        _clock.Advance(TimeSpan.FromDays(5));
        (await sut.TouchAsync(default)).Status.Should().Be(TrialStatus.Active); // records max-observed = +5d

        _clock.Set(BaseTime.AddDays(2)); // clock rolled backwards below max-observed.
        var rolledBack = await sut.EvaluateAsync(default);

        rolledBack.Status.Should().Be(TrialStatus.Tampered);
        rolledBack.TamperReason.Should().Contain("backwards");
    }

    [Fact]
    public async Task Touch_WhenTampered_DoesNotRewriteStores()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sut = CreateSut();
        await sut.StartAsync(default);
        File.Delete(_tmp.File(".trial-marker"));

        var result = await sut.TouchAsync(default);

        // Tampered → Touch не должен «лечить» состояние перезаписью маркера.
        result.Status.Should().Be(TrialStatus.Tampered);
        File.Exists(_tmp.File(".trial-marker")).Should().BeFalse();
    }

    private byte[]? ReadRegistryValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        return key?.GetValue("Trial") as byte[];
    }

    private void DeleteRegistryValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        key?.DeleteValue("Trial", throwOnMissingValue: false);
    }

    public void Dispose()
    {
        _tmp.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Set(DateTimeOffset now) => _now = now;
    }
}
