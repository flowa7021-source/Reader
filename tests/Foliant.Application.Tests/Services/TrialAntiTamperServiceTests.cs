using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class TrialAntiTamperServiceTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_AllStoresEmpty_ReturnsNotStarted_With30Days()
    {
        var result = TrialAntiTamperService.Evaluate(null, null, null, BaseTime);

        result.Status.Should().Be(TrialStatus.NotStarted);
        result.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays);
        result.TamperReason.Should().BeNull();
    }

    [Fact]
    public void Evaluate_FreshlyStarted_ReturnsActive_FullDays()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var marker = TrialAntiTamperService.ComputeMarker(state);

        var result = TrialAntiTamperService.Evaluate(state, state, marker, BaseTime);

        result.Status.Should().Be(TrialStatus.Active);
        result.DaysRemaining.Should().Be(TrialAntiTamperService.TrialDays);
    }

    [Fact]
    public void Evaluate_AfterTenDays_CountsDown()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var marker = TrialAntiTamperService.ComputeMarker(state);
        var now = BaseTime.AddDays(10);

        var result = TrialAntiTamperService.Evaluate(state, state, marker, now);

        result.Status.Should().Be(TrialStatus.Active);
        result.DaysRemaining.Should().Be(20);
    }

    [Fact]
    public void Evaluate_PastTrialPeriod_ReturnsExpired()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var marker = TrialAntiTamperService.ComputeMarker(state);
        var now = BaseTime.AddDays(31);

        var result = TrialAntiTamperService.Evaluate(state, state, marker, now);

        result.Status.Should().Be(TrialStatus.Expired);
        result.DaysRemaining.Should().Be(0);
    }

    [Fact]
    public void Evaluate_PrimaryMissing_ReturnsTampered()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var marker = TrialAntiTamperService.ComputeMarker(state);

        var result = TrialAntiTamperService.Evaluate(null, state, marker, BaseTime);

        result.Status.Should().Be(TrialStatus.Tampered);
        result.TamperReason.Should().Contain("missing");
    }

    [Fact]
    public void Evaluate_SecondaryMissing_ReturnsTampered()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var marker = TrialAntiTamperService.ComputeMarker(state);

        var result = TrialAntiTamperService.Evaluate(state, null, marker, BaseTime);

        result.Status.Should().Be(TrialStatus.Tampered);
    }

    [Fact]
    public void Evaluate_MarkerMissing_ReturnsTampered()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);

        var result = TrialAntiTamperService.Evaluate(state, state, markerHash: null, BaseTime);

        result.Status.Should().Be(TrialStatus.Tampered);
    }

    [Fact]
    public void Evaluate_StatesDiverge_ReturnsTampered()
    {
        var primary = TrialAntiTamperService.NewTrial(BaseTime);
        var secondary = primary with { StartedAt = BaseTime.AddDays(-10) }; // tampered: pretend started earlier
        var marker = TrialAntiTamperService.ComputeMarker(primary);

        var result = TrialAntiTamperService.Evaluate(primary, secondary, marker, BaseTime);

        result.Status.Should().Be(TrialStatus.Tampered);
        result.TamperReason.Should().Contain("diverge");
    }

    [Fact]
    public void Evaluate_MarkerMismatch_ReturnsTampered()
    {
        var state = TrialAntiTamperService.NewTrial(BaseTime);
        var bogusMarker = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = TrialAntiTamperService.Evaluate(state, state, bogusMarker, BaseTime);

        result.Status.Should().Be(TrialStatus.Tampered);
        result.TamperReason.Should().Contain("Marker");
    }

    [Fact]
    public void Evaluate_SystemClockMovedBackwards_ReturnsTampered()
    {
        var startTime = BaseTime;
        var observed = BaseTime.AddDays(5);
        var state = new TrialState(startTime, observed, "abc");
        var marker = TrialAntiTamperService.ComputeMarker(state);

        // имитируем откат часов: now < MaxObservedAt
        var rolledBack = BaseTime.AddDays(2);

        var result = TrialAntiTamperService.Evaluate(state, state, marker, rolledBack);

        result.Status.Should().Be(TrialStatus.Tampered);
        result.TamperReason.Should().Contain("backwards");
    }

    [Fact]
    public void Evaluate_TakesMaxObservedAcrossStores()
    {
        // primary имеет более старый MaxObservedAt, secondary — более свежий.
        // Tamper-checker должен использовать max — иначе кто-то занизит primary
        // и протащит "now" между ними.
        var primary = new TrialState(BaseTime, BaseTime.AddDays(2), "n");
        var secondary = new TrialState(BaseTime, BaseTime.AddDays(8), "n");
        var marker = TrialAntiTamperService.ComputeMarker(primary);

        var result = TrialAntiTamperService.Evaluate(primary, secondary, marker, BaseTime.AddDays(5));

        result.Status.Should().Be(TrialStatus.Tampered);   // 5 дней < 8 (max)
    }

    [Fact]
    public void UpdateMaxObserved_NewerThanCurrent_ReturnsAdvanced()
    {
        var state = new TrialState(BaseTime, BaseTime.AddDays(2), "n");

        var updated = TrialAntiTamperService.UpdateMaxObserved(state, BaseTime.AddDays(5));

        updated.MaxObservedAt.Should().Be(BaseTime.AddDays(5));
        updated.StartedAt.Should().Be(state.StartedAt);
        updated.Nonce.Should().Be(state.Nonce);
    }

    [Fact]
    public void UpdateMaxObserved_OlderThanCurrent_KeepsMaxIntact()
    {
        var state = new TrialState(BaseTime, BaseTime.AddDays(5), "n");

        var updated = TrialAntiTamperService.UpdateMaxObserved(state, BaseTime.AddDays(2));

        updated.Should().BeSameAs(state);
    }

    [Fact]
    public void ComputeMarker_DependsOn_StartedAtAndNonce_NotMaxObserved()
    {
        var a = new TrialState(BaseTime, BaseTime, "abc");
        var sameKey = new TrialState(BaseTime, BaseTime.AddDays(10), "abc");   // different MaxObserved
        var diffNonce = new TrialState(BaseTime, BaseTime, "def");
        var diffStart = new TrialState(BaseTime.AddSeconds(1), BaseTime, "abc");

        TrialAntiTamperService.ComputeMarker(a).Should().Be(TrialAntiTamperService.ComputeMarker(sameKey));
        TrialAntiTamperService.ComputeMarker(a).Should().NotBe(TrialAntiTamperService.ComputeMarker(diffNonce));
        TrialAntiTamperService.ComputeMarker(a).Should().NotBe(TrialAntiTamperService.ComputeMarker(diffStart));
    }

    [Fact]
    public void NewTrial_HasFreshNonce_AndNowAsStartAndMax()
    {
        var t1 = TrialAntiTamperService.NewTrial(BaseTime);
        var t2 = TrialAntiTamperService.NewTrial(BaseTime);

        t1.StartedAt.Should().Be(BaseTime);
        t1.MaxObservedAt.Should().Be(BaseTime);
        t1.Nonce.Should().NotBeNullOrEmpty();
        t1.Nonce.Should().NotBe(t2.Nonce, "каждый вызов даёт свежий GUID-нонс");
    }
}
